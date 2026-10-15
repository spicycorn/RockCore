//! RockCore 桌面壳。
//!
//! 设计约束：Rust 侧**零业务逻辑**——判定、解析、校验全部在 TypeScript 引擎中完成。
//! 这里只提供：SQLite 单连接访问（db_execute / db_select / db_transaction）、
//! 系统对话框（plugin-dialog）、以及通用文件字节读写与内置模板读取命令。
//!
//! 为什么自管 SQLite 连接、不再使用 tauri-plugin-sql：
//! plugin-sql 底层是 5 连接池，每条语句独立从池里取连接，手工 BEGIN/COMMIT
//! 可能被分到不同连接——事务会永久悬挂在某个连接上，之后任何落在这个连接上的
//! BEGIN 都会报 "cannot start a transaction within a transaction"（v1.1.0
//! 导入报错的根因）。这里改为**单连接 + 异步互斥锁**：
//! · 所有语句串行经过同一个连接；
//! · db_transaction 全程持锁跨 BEGIN…COMMIT，事务之间不可能交错；
//! · db_execute / db_select 直接拒绝 BEGIN/COMMIT/ROLLBACK——事务控制只能
//!   走 db_transaction，从结构上杜绝同类问题。

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use serde::Deserialize;
use serde_json::Value;
use sqlx::sqlite::{SqliteConnectOptions, SqliteConnection};
use sqlx::{Connection, Row};
use std::fs;
use std::path::{Path, PathBuf};
use tauri::Manager;
use tokio::sync::Mutex;

/// 应用数据库：单连接 + 互斥锁（锁跨整个命令/事务，天然串行）。
struct Db {
    conn: Mutex<SqliteConnection>,
}

/// setup 阶段的打开结果；失败时把原因透传给前端（initError 界面可见），不让应用闪退。
type DbState = Result<Db, String>;

type Query<'a> = sqlx::query::Query<'a, sqlx::sqlite::Sqlite, sqlx::sqlite::SqliteArguments<'a>>;

/// 事务语句（前端 repos 层构造）。
#[derive(Deserialize)]
struct Stmt {
    sql: String,
    #[serde(default)]
    params: Vec<Value>,
}

fn err<E: std::fmt::Display>(e: E) -> String {
    e.to_string()
}

/// 拒绝裸事务控制语句：事务只能由 db_transaction 发起（防"事务套事务"复发的硬约束）。
fn check_no_tx_control(sql: &str) -> Result<(), String> {
    let head = sql.trim_start();
    let word: String = head
        .chars()
        .take_while(|c| c.is_alphabetic())
        .collect::<String>()
        .to_ascii_uppercase();
    if matches!(word.as_str(), "BEGIN" | "COMMIT" | "ROLLBACK" | "END") {
        return Err(
            "事务控制语句不能单独执行：请通过 db_transaction 提交整组语句".to_string(),
        );
    }
    Ok(())
}

async fn lock<'s>(
    state: &'s tauri::State<'_, DbState>,
) -> Result<tokio::sync::MutexGuard<'s, SqliteConnection>, String> {
    match state.as_ref() {
        Ok(db) => Ok(db.conn.lock().await),
        Err(e) => Err(format!("数据库不可用：{e}")),
    }
}

/// 把 JSON 参数按类型绑定到语句：null→NULL，整数→INTEGER，小数→REAL，其余→TEXT。
fn bind_all<'a>(mut q: Query<'a>, params: &'a [Value]) -> Query<'a> {
    for p in params {
        q = match p {
            Value::Null => q.bind(Option::<i64>::None),
            Value::Bool(b) => q.bind(*b as i64),
            Value::Number(n) => match n.as_i64() {
                Some(i) => q.bind(i),
                None => q.bind(n.as_f64().unwrap_or(0.0)),
            },
            Value::String(s) => q.bind(s.clone()),
            other => q.bind(other.to_string()),
        };
    }
    q
}

/// 执行一条语句（自动提交）。返回 { lastInsertId, rowsAffected }。
#[tauri::command]
async fn db_execute(
    state: tauri::State<'_, DbState>,
    sql: String,
    params: Vec<Value>,
) -> Result<Value, String> {
    check_no_tx_control(&sql)?;
    let mut conn = lock(&state).await?;
    let res = bind_all(sqlx::query(&sql), &params)
        .execute(&mut *conn)
        .await
        .map_err(|e| format!("SQL 执行失败：{e}（语句：{}）", sql.trim()))?;
    Ok(serde_json::json!({
        "lastInsertId": res.last_insert_id(),
        "rowsAffected": res.rows_affected(),
    }))
}

/// 查询。行以 JSON 对象数组返回（列名→值）。
#[tauri::command]
async fn db_select(
    state: tauri::State<'_, DbState>,
    sql: String,
    params: Vec<Value>,
) -> Result<Vec<Value>, String> {
    check_no_tx_control(&sql)?;
    let mut conn = lock(&state).await?;
    let rows = bind_all(sqlx::query(&sql), &params)
        .fetch_all(&mut *conn)
        .await
        .map_err(|e| format!("查询失败：{e}（语句：{}）", sql.trim()))?;
    rows.into_iter()
        .map(|r| r.try_into_json().map_err(err))
        .collect()
}

/// 单事务执行一组语句：全程持有连接锁，BEGIN…COMMIT 之间不会有任何其他语句插入，
/// 任一失败整体回滚。返回最后一条 INSERT 的自增 id（无 INSERT 时为 0）。
#[tauri::command]
async fn db_transaction(
    state: tauri::State<'_, DbState>,
    statements: Vec<Stmt>,
) -> Result<u64, String> {
    let mut conn = lock(&state).await?;
    if statements.is_empty() {
        return Ok(0);
    }
    sqlx::query("BEGIN IMMEDIATE")
        .execute(&mut *conn)
        .await
        .map_err(|e| format!("开启事务失败：{e}"))?;
    let mut last_id: u64 = 0;
    for st in &statements {
        match bind_all(sqlx::query(&st.sql), &st.params)
            .execute(&mut *conn)
            .await
        {
            Ok(r) => {
                if r.last_insert_id() > 0 {
                    last_id = r.last_insert_id();
                }
            }
            Err(e) => {
                let _ = sqlx::query("ROLLBACK").execute(&mut *conn).await;
                return Err(format!("事务执行失败，已整体回滚：{e}（语句：{}）", st.sql.trim()));
            }
        }
    }
    if let Err(e) = sqlx::query("COMMIT").execute(&mut *conn).await {
        let _ = sqlx::query("ROLLBACK").execute(&mut *conn).await;
        return Err(format!("提交事务失败：{e}"));
    }
    Ok(last_id)
}

/// 目录可写探测：写一个探针文件再删除。
fn dir_writable(dir: &Path) -> bool {
    let probe = dir.join(".rockcore-write-test");
    match fs::OpenOptions::new().create(true).append(true).open(&probe) {
        Ok(_) => {
            let _ = fs::remove_file(&probe);
            true
        }
        Err(_) => false,
    }
}

/// 数据库文件位置：**优先软件根目录**（exe 同目录）。NSIS 卸载器只删除安装清单
/// 内的文件，运行期生成的 rockcore.db 不会被卸载删除，升级/重装数据不丢。
/// 软件根目录不可写（如装进 Program Files 且无权限）时退回应用数据目录。
/// 旧版数据库若在应用数据目录而新位置没有，则整体搬迁（含 -wal/-shm 边文件；
/// 跨盘符 rename 失败改复制，并把原文件改名 .db.bak 防止二次打开旧库）。
fn resolve_db_path(app: &tauri::AppHandle) -> PathBuf {
    let appdata = app.path().app_config_dir().ok();
    let exe_dir = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.to_path_buf()));
    let dir = exe_dir
        .filter(|d| dir_writable(d))
        .or_else(|| appdata.clone())
        .unwrap_or_else(|| PathBuf::from("."));
    if let Some(ad) = &appdata {
        let old = ad.join("rockcore.db");
        let new = dir.join("rockcore.db");
        if dir != *ad && !new.exists() && old.exists() {
            for side in ["", "-wal", "-shm"] {
                let from = PathBuf::from(format!("{}{}", old.display(), side));
                let to = PathBuf::from(format!("{}{}", new.display(), side));
                if !from.exists() {
                    continue;
                }
                if fs::rename(&from, &to).is_err()
                    && fs::copy(&from, &to).is_ok()
                {
                    let mut bak = from.clone();
                    bak.set_extension("db.bak");
                    let _ = fs::rename(&from, &bak);
                }
            }
        }
    }
    dir.join("rockcore.db")
}

/// 读取任意路径的字节（前端已通过系统文件对话框取得用户明确选择的路径）。
#[tauri::command]
fn read_file_bytes(path: PathBuf) -> Result<Vec<u8>, String> {
    fs::read(&path).map_err(|e| format!("读取文件失败：{e}"))
}

/// 写入字节到任意路径（前端已通过系统另存为对话框取得用户明确选择的路径）。
#[tauri::command]
fn write_file_bytes(path: PathBuf, bytes: Vec<u8>) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|e| format!("创建目录失败：{e}"))?;
    }
    fs::write(&path, bytes).map_err(|e| format!("写入文件失败：{e}"))
}

/// 读取随安装包附带的导入模板（打包资源目录）。
#[tauri::command]
fn bundled_template(app: tauri::AppHandle) -> Result<Vec<u8>, String> {
    let dir = app
        .path()
        .resource_dir()
        .map_err(|e| format!("定位资源目录失败：{e}"))?;
    let file = dir.join("resources").join("岩心回次统计导入模板.xlsx");
    fs::read(&file).map_err(|e| format!("读取内置模板失败：{e}"))
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let path = resolve_db_path(&app.handle().clone());
            let state: DbState = tauri::async_runtime::block_on(async move {
                let opts = SqliteConnectOptions::new()
                    .filename(&path)
                    .create_if_not_exists(true)
                    .busy_timeout(std::time::Duration::from_secs(5));
                SqliteConnection::connect_with(&opts)
                    .await
                    .map(|conn| Db { conn: Mutex::new(conn) })
                    .map_err(|e| format!("打开数据库失败（{}）：{e}", path.display()))
            });
            app.manage(state);
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            read_file_bytes,
            write_file_bytes,
            bundled_template,
            db_execute,
            db_select,
            db_transaction
        ])
        .run(tauri::generate_context!())
        .expect("RockCore 启动失败");
}
