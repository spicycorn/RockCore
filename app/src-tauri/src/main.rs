//! RockCore 桌面壳。
//!
//! 设计约束：Rust 侧**零业务逻辑**——判定、解析、校验全部在 TypeScript 引擎中完成，
//! 这里只提供三件事：SQLite 驱动（plugin-sql）、系统对话框（plugin-dialog）、
//! 以及两个通用文件字节读写命令（供前端读取用户选择的 Excel / 另存导入模板）。

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::fs;
use std::path::PathBuf;

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
    use tauri::Manager;
    let dir = app
        .path()
        .resource_dir()
        .map_err(|e| format!("定位资源目录失败：{e}"))?;
    let file = dir.join("resources").join("岩心回次统计导入模板.xlsx");
    fs::read(&file).map_err(|e| format!("读取内置模板失败：{e}"))
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_sql::Builder::default().build())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            read_file_bytes,
            write_file_bytes,
            bundled_template
        ])
        .run(tauri::generate_context!())
        .expect("RockCore 启动失败");
}
