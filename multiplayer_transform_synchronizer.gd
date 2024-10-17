# The purpose of this script is to synchronize position, rotation and scale
# using frame interpolation (so that the object does not twitch at low server TPS)

extends Node

@export_group("What to sync? (main node)")
## The node whose parameters will be synchronized
@export var track_this_object: Node3D # Сделать проверку на то существует ли этот обьект

@export_group("What to sync?")
@export var sync_position := true
@export var sync_rotation := true
@export var sync_scale := true

@export_group("Min-Max acceptable delay in the client")
## Minimum threshold for interpolation_offset_ms
@export_range(1, 10)  var interpolation_offset_min := 1
## Maximum threshold for interpolation_offset_ms (500 - quite enough even for a very lagging server)
@export_range(20, 500) var interpolation_offset_max := 500

var sleep_mode := false;
var sleep_mode_information_delivered := false

var old_position = Vector3();
var old_rotation = Vector3();
var old_scale = Vector3();

var transorm_state_buffer = [] # [old_State (0), future_state (1)] - buffer for interpolation, stores old and new data
var interpolation_offset_ms := 100 # The current time in milliseconds, which the game is interpolated (depends on the speed of receiving data from the server and on the server load) this is necessary for smooth movement, and this value is changed by the algorithm depending on the speed of receiving data from the server and on its load

func _ready() -> void:
	if track_this_object == null:
		assert(false, "Add a node for 'track_this_object' in the inspector")
	if !multiplayer.is_server():
		_request_transform.rpc()

func _process(delta: float) -> void:
	if multiplayer.is_server():
		return
	
	var render_time := get_current_unix_time_ms() - interpolation_offset_ms

	if transorm_state_buffer.size() > 1:
		while transorm_state_buffer.size() > 2 and render_time > transorm_state_buffer[1].snap_time_ms:
			transorm_state_buffer.remove_at(0)
			
		var interpolation_factor := float(render_time - transorm_state_buffer[0].snap_time_ms) / float(transorm_state_buffer[1].snap_time_ms - transorm_state_buffer[0].snap_time_ms)
		
		if transorm_state_buffer[1].sleep_mode == true:
			if sync_position:
				track_this_object.position = transorm_state_buffer[1].position
			if sync_rotation:
				track_this_object.rotation = transorm_state_buffer[1].rotation
			if sync_scale:
				track_this_object.scale = transorm_state_buffer[1].scale
			transorm_state_buffer[1].snap_time_ms = render_time
			return
		
		recalculate_interpolation_offset_ms(interpolation_factor)
		
		if sync_position:
			track_this_object.position = lerp(transorm_state_buffer[0].position, transorm_state_buffer[1].position, interpolation_factor)
		if sync_rotation:
			track_this_object.rotation = lerp(transorm_state_buffer[0].rotation, transorm_state_buffer[1].rotation, interpolation_factor)
		if sync_scale:
			track_this_object.scale = lerp(transorm_state_buffer[0].scale, transorm_state_buffer[1].scale, interpolation_factor)
		
func recalculate_interpolation_offset_ms(interpolation_factor: float):
	if interpolation_factor > 1 && interpolation_offset_ms < interpolation_offset_max:
		interpolation_offset_ms += 1
	else:
		if transorm_state_buffer.size() > 2 && interpolation_offset_ms > interpolation_offset_min:
			interpolation_offset_ms -= 1

func get_current_unix_time_ms() -> int:
	return (Time.get_unix_time_from_system() * 1000)

func _physics_process(delta: float) -> void:
	if multiplayer.is_server():
		sync_transform()

func sync_transform() -> void:
	var at_least_one_has_been_changed := false
	
	if sync_position && track_this_object.position != old_position:
		at_least_one_has_been_changed = true
		
	if sync_rotation && track_this_object.rotation != old_rotation:
		at_least_one_has_been_changed = true
		
	if sync_scale && track_this_object.scale != old_scale:
		at_least_one_has_been_changed = true
	
	# If none of the synchronized data values ​​have been changed, we enter sleep mode
	sleep_mode = not at_least_one_has_been_changed;
	
	if sleep_mode && sleep_mode_information_delivered:
		return
	
	sleep_mode_information_delivered = false
	
	_sync_transform.rpc(
		track_this_object.position if sync_position else Vector3(),
		track_this_object.rotation if sync_rotation else Vector3(),
		track_this_object.scale if sync_scale else Vector3(),
		get_current_unix_time_ms(),
		sleep_mode
	)
	
	if sync_position:
		old_position = track_this_object.position
		
	if sync_rotation:
		old_rotation = track_this_object.rotation
		
	if sync_scale:
		old_scale = track_this_object.scale
		
	if sleep_mode:
		sleep_mode_information_delivered = true

@rpc("authority", "call_remote", "unreliable_ordered")
func _sync_transform(new_position: Vector3, new_rotation: Vector3, new_scale: Vector3, snap_time_ms: int, sleep_mode_ = false) -> void:
	var snap = {
		"snap_time_ms": snap_time_ms,
		"position": new_position,
		"rotation": new_rotation,
		"scale": new_scale,
		"sleep_mode": sleep_mode_
	}
	transorm_state_buffer.append(snap)

@rpc("authority", "call_remote", "reliable")
func _respone_transform(new_position: Vector3, new_rotation: Vector3, new_scale: Vector3) -> void:
	if sync_position:
		track_this_object.position = new_position
	if sync_rotation:
		track_this_object.rotation = new_rotation
	if sync_scale:
		track_this_object.scale = new_scale
	
@rpc("any_peer", "call_remote", "reliable")
func _request_transform() -> void:
	if multiplayer.is_server():
		_respone_transform.rpc(
			track_this_object.position if sync_position else Vector3(),
			track_this_object.rotation if sync_rotation else Vector3(),
			track_this_object.scale if sync_scale else Vector3()
		)