extends Node2D

# GodotWebViewSharp 演示
# 对标 godot_wry 的 example.tscn

@onready var _webview := $Window/WebView

func _ready():
	if Engine.is_editor_hint():
		return

func _on_button_pressed():
	if _webview != null and _webview.has_method("load_url"):
		_webview.load_url("res://Demo/character_creator/ui/index.html")