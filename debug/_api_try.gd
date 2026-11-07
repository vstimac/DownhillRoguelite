extends SceneTree
func _init():
	run()
func run():
	var t = Time.get_ticks_msec()
	var model = NobodyWhoModel.create("D:/Game dev/Models/qwen2.5-3b-instruct-q4_k_m.gguf", {})
	print("model: ", model, " type ", typeof(model), " ", Time.get_ticks_msec() - t, " ms")
	if model is Signal:
		model = await model
		print("awaited model: ", model)
	var chat = NobodyWhoChat.create(model, {"system_prompt": "You are a terse mountain guide. Answer in one sentence."})
	print("chat: ", chat, " type ", typeof(chat))
	if chat is Signal:
		chat = await chat
		print("awaited chat: ", chat)
	var stream = chat.ask("What should I carry down a mountain?")
	print("stream: ", stream, " type ", typeof(stream))
	if stream is Signal:
		stream = await stream
	var tok = stream.next_token()
	print("tok: ", tok, " type ", typeof(tok))
	var out = ""
	var n = 0
	while n < 200:
		var x = stream.next_token()
		if x is Signal:
			x = await x
		if x == null:
			break
		out += str(x)
		n += 1
	print("OUT: ", out, " (", Time.get_ticks_msec() - t, " ms)")
	quit()
