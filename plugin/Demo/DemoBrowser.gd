extends Node2D

# GodotWebViewSharp 演示
# 对标 godot_wry 的 example.tscn

@onready var _webview := $Window/WebView

func _ready():
	if Engine.is_editor_hint():
		return

func _on_button_pressed():
	# C# 脚本的方法按原名暴露给 GDScript（has_method 也要用原名，否则静默不执行）
	if _webview != null and _webview.has_method("LoadUrl"):
		_webview.LoadUrl("res://Demo/character_creator/ui/index.html")