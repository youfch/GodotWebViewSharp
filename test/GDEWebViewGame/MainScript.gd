extends Control

@onready var webview = $WebView
@onready var url_input = $UrlInput
@onready var go_btn = $GoBtn
@onready var back_btn = $BackBtn
@onready var forward_btn = $ForwardBtn
@onready var reload_btn = $ReloadBtn
@onready var status_label = $StatusLabel

func _ready():
	# 当 GDExtension 注册了 WebViewControl 类型后，
	# 可以通过以下方式使用：
	# var wv = WebViewControl.new()
	# add_child(wv)
	
	go_btn.pressed.connect(_on_go)
	back_btn.pressed.connect(_on_back)
	forward_btn.pressed.connect(_on_forward)
	reload_btn.pressed.connect(_on_reload)
	url_input.text_submitted.connect(_on_go)
	
	status_label.text = "Ready (GDExtension mode)"

func _on_go():
	var url = url_input.text.strip_edges()
	if url == "":
		return
	if not url.begins_with("http://") and not url.begins_with("https://"):
		url = "https://" + url
	# webview.load_url(url)  # 待 GDExtension 加载后启用
	status_label.text = "Loading: " + url

func _on_back():
	# webview.go_back()
	pass

func _on_forward():
	# webview.go_forward()
	pass

func _on_reload():
	# webview.reload()
	pass