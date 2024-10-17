using Godot;
using System.Collections.Generic;

public partial class MultiplayerTransformSynchronizer : Node
{
	[ExportGroup("What to sync? (main node)")]
	[Export] public Node3D TrackThisObject { get; set; }

	[ExportGroup("What to sync?")]
	[Export] public bool SyncPosition = true;
	[Export] public bool SyncRotation = true;
	[Export] public bool SyncScale = true;

	[ExportGroup("Min-Max acceptable delay in the client")]
	[Export(PropertyHint.Range, "1,10,")] public int InterpolationOffsetMin = 1;
	[Export(PropertyHint.Range, "20,5000,")] public int InterpolationOffsetMax = 3000;

	private bool _sleepMode = false;
	private bool _sleepModeInformationDelivered = false;

	private Vector3 _oldPosition = Vector3.Zero;
	private Vector3 _oldRotation = Vector3.Zero;
	private Vector3 _oldScale = Vector3.Zero;

	private List<MultiplayerTransformState> _transformStateBuffer = new List<MultiplayerTransformState>();
	private int _currentInterpolationOffsetMs = 100;

	public override void _Ready()
	{
		base._Ready();

		if (TrackThisObject == null)
		{
			GD.PrintErr("Add a node for 'TrackThisObject' in the inspector");
		}

		if (!Multiplayer.IsServer())
		{
			Rpc("_RequestTransform");
		}

		SetPhysicsProcess(Multiplayer.IsServer());
		SetProcess(!Multiplayer.IsServer());
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (Multiplayer.IsServer())
			return;

		double renderTime = GetUnixTimeMs() - _currentInterpolationOffsetMs;

		if (_transformStateBuffer.Count > 1)
		{
			while (_transformStateBuffer.Count > 2 && renderTime > _transformStateBuffer[1].SnapTimeMs)
			{
				_transformStateBuffer.RemoveAt(0);
			}

			float interpolationFactor = (float)((renderTime - _transformStateBuffer[0].SnapTimeMs) / (_transformStateBuffer[1].SnapTimeMs - _transformStateBuffer[0].SnapTimeMs));
		
			if (_transformStateBuffer[1].SleepMode == true)
			{
				if (SyncPosition)
					TrackThisObject.Position = _transformStateBuffer[1].Position;

				if (SyncRotation)
					TrackThisObject.Rotation = _transformStateBuffer[1].Rotation;

				if (SyncScale)
					TrackThisObject.Scale = _transformStateBuffer[1].Scale;

				MultiplayerTransformState transformState = _transformStateBuffer[1];
				transformState.SnapTimeMs = renderTime;
				_transformStateBuffer[1] = transformState;

				return;
			}

			RecalculateInterpolationOffsetMs(interpolationFactor);

			if (SyncPosition)
				TrackThisObject.Position = Vector3Lerp(_transformStateBuffer[0].Position, _transformStateBuffer[1].Position, interpolationFactor);

			if (SyncRotation)
				TrackThisObject.Rotation = Vector3Lerp(_transformStateBuffer[0].Rotation, _transformStateBuffer[1].Rotation, interpolationFactor);

			if (SyncScale)
				TrackThisObject.Scale = Vector3Lerp(_transformStateBuffer[0].Scale, _transformStateBuffer[1].Scale, interpolationFactor);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);

		SyncTransform();
	}

	private void SyncTransform()
	{
		if (!Multiplayer.IsServer())
			return;

		bool atLeatOneHasBeenChanged = false;

		if (SyncPosition && TrackThisObject.Position != _oldPosition)
			atLeatOneHasBeenChanged = true;

		if (SyncRotation && TrackThisObject.Rotation != _oldRotation)
			atLeatOneHasBeenChanged = true;

		if (SyncScale && TrackThisObject.Scale != _oldScale)
			atLeatOneHasBeenChanged = true;

		_sleepMode = !atLeatOneHasBeenChanged;

		if (_sleepMode && _sleepModeInformationDelivered)
			return;

		_sleepModeInformationDelivered = false;

		Rpc("_SyncTransform", 
			SyncPosition ? TrackThisObject.Position : Vector3.Zero,
			SyncRotation ? TrackThisObject.Rotation : Vector3.Zero,
			SyncScale ? TrackThisObject.Scale : Vector3.Zero,
			GetUnixTimeMs(),
			_sleepMode);

		if (SyncPosition)
			_oldPosition = TrackThisObject.Position;

		if (SyncRotation)
			_oldRotation = TrackThisObject.Rotation;

		if (SyncScale)
			_oldScale = TrackThisObject.Scale;

		if (_sleepMode)
			_sleepModeInformationDelivered = true;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void _SyncTransform(Vector3 newPosition, Vector3 newRotation, Vector3 newScale, double snapTimeMs, bool sleepMode)
	{
		MultiplayerTransformState state = new MultiplayerTransformState();

		state.Position = newPosition;
		state.Rotation = newRotation;
		state.Scale = newScale;
		state.SnapTimeMs = snapTimeMs;
		state.SleepMode = sleepMode;

		_transformStateBuffer.Add(state);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void _RequestTransform()
	{
		if (Multiplayer.IsServer())
		{
			Vector3 newPosition = SyncPosition ? TrackThisObject.Position : Vector3.Zero;
			Vector3 newRotation = SyncRotation ? TrackThisObject.Rotation : Vector3.Zero;
			Vector3 newScale = SyncScale ? TrackThisObject.Scale : Vector3.Zero;
			Rpc("_ResponceTransform", newPosition, newRotation, newScale);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void _ResponceTransform(Vector3 newPosition, Vector3 newRotation, Vector3 newScale)
	{
		if (SyncPosition)
			TrackThisObject.Position = newPosition;

		if (SyncRotation)
			TrackThisObject.Rotation = newRotation;

		if (SyncScale)
			TrackThisObject.Scale = newScale;
	}

	private void RecalculateInterpolationOffsetMs(float interpolationFactor)
	{
		if (interpolationFactor > 1 && _currentInterpolationOffsetMs < InterpolationOffsetMax)
		{
			_currentInterpolationOffsetMs += 1;
		}
		else
		{
			if (_transformStateBuffer.Count > 2 && _currentInterpolationOffsetMs > InterpolationOffsetMin)
			{
				_currentInterpolationOffsetMs -= 1;
			}
		}
	}

	private Vector3 Vector3Lerp(Vector3 First, Vector3 Second, float Amount)
	{
		float retX = Mathf.Lerp(First.X, Second.X, Amount);
		float retY = Mathf.Lerp(First.Y, Second.Y, Amount);
		float retZ = Mathf.Lerp(First.Z, Second.Z, Amount);

		return new Vector3(retX, retY, retZ);
	}

	private double GetUnixTimeMs()
	{
		return Time.GetUnixTimeFromSystem() * 1000;
	}
}

public struct MultiplayerTransformState
{
	public MultiplayerTransformState()
	{

	}

	public double SnapTimeMs = 0;
	public bool SleepMode = false;
	public Vector3 Position = Vector3.Zero;
	public Vector3 Rotation = Vector3.Zero;
	public Vector3 Scale = Vector3.Zero;
}