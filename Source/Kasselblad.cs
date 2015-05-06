using UnityEngine;
using System;

using KSP.IO;

namespace Kasselblad
{
	public class Kasselblad : PartModule
	{

		/*** Common Methods ***/

		public override void OnStart (StartState state)
		{
			if (state != StartState.Editor) {
				
				// Set up the camera.
				InitRTT();

				// Initialize the action state
				Events["toggleOnOff"].guiName = _camEnabled ? "Switch Off" : "Switch On";

				// Create our windows. TODO: Should we do this in the editor too, for preview?
				if (_windowStyle == null) InitStyles();
				RenderingManager.AddToPostDrawQueue(0, OnDraw);

			}
		}

		public override void OnUpdate()
		{
			base.OnUpdate();
			
			GameScenes scene = HighLogic.LoadedScene;
			if (scene != GameScenes.FLIGHT)
				return;


			// TODO: Any camera animation should update here.

		}

		/*** Settings management ***/
		
		public override void OnSave (ConfigNode node)
		{
			PluginConfiguration config = PluginConfiguration.CreateForType<Kasselblad>();
			config.SetValue ("Window Position", _windowPosition);
			config.SetValue ("Camera Enabled", _camEnabled);

			config.save ();	
		}
		
		public override void OnLoad (ConfigNode node)
		{
			PluginConfiguration config = PluginConfiguration.CreateForType<Kasselblad>();
			config.load ();

			_windowPosition = config.GetValue<Rect> ("Window Position");
			_camEnabled = config.GetValue<bool> ("Camera Enabled");
		}

		/*** Camera code! ***/

		private static string[] GameCameras = {
			"GalaxyCamera", "Camera ScaledSpace", "Camera 01", "Camera 00", "Camera VE Underlay", "Camera VE Overlay"
		};

		private RenderTexture _camTex;
		private GameObject[] _camobjs = new GameObject[GameCameras.Length];
		private Camera[] _cams = new Camera[GameCameras.Length];
		private bool _camEnabled = true;

		// Shader needed to fix the camera's output before processing
		private Material dealphaMaterial = new Material (
			"Shader \"Hidden/Dealpha\" {" +
			"   Properties {" +
			"       _MainTex (\"MainTex\", 2D) = \"white\" {}" +
			"   }" +
			"   SubShader {" +
			"    Pass {" +
			"        ZTest Always Cull Off ZWrite Off" +
			"        ColorMask A" +
			"        SetTexture [_MainTex] {" +
			"            constantColor(0,0,0,1) combine constant }" +
			"    }" +
			"   }" +
			"}"
		);

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "FoV"),
		 UI_FloatRange(minValue = 45.0f, maxValue = 90.0f, stepIncrement = 1.0f)]
		public float camFoV = 60.0f;


		private void InitRTT() {
			_camTex = new RenderTexture (1024, 768, 1, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB);
			_camTex.isPowerOfTwo = false;
			_camTex.Create ();

			for (int i = 0; i < GameCameras.Length; i++) SetupCamera (i);
		}

		private void SetupCamera(int i) {
			
			Camera existing = FindCamera (GameCameras [i]);
			if (existing == null)
				return;

			_camobjs [i] = new GameObject ("Kasselblad-" + GameCameras [i]);
			_cams [i] = _camobjs[i].AddComponent<Camera> ();
			_cams [i].CopyFrom(existing);
				
			_cams [i].fieldOfView = camFoV;
			_cams [i].targetTexture = _camTex;
			_cams [i].enabled = _camEnabled;
		}

		virtual public void RenderCam(Vector3 pos, Vector3 aim, Quaternion rot) {
			RenderTexture currentRT = RenderTexture.active;
			RenderTexture.active = _camTex;


			for (int i = 0; i < _cams.Length; i++) {
				Camera cam = _cams[i];
				if (cam == null) continue;

				if (i == 2 || i == 3) {
					cam.transform.position = pos;
				}

				cam.transform.forward = aim;
				cam.transform.rotation = rot;

				cam.fieldOfView = camFoV;
				cam.targetTexture = _camTex;

				cam.Render();
			}



			Graphics.Blit(_camTex, _camTex, dealphaMaterial);

			RenderTexture.active = currentRT;
		}

		private Camera FindCamera(string name) {
			foreach(Camera camera in Camera.allCameras) {
				if(camera.name == name)
					return camera;
			}
			return null;
		}

		/*** Events ***/
		[KSPEvent(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 20.0f, guiActiveEditor = false, guiName = "Switch On")]
		public void toggleOnOff()
		{
			_camEnabled = !_camEnabled;
			
			for (int i = 0; i < 3; i++)
				_cams [i].enabled = _camEnabled;

			Events["toggleOnOff"].guiName = _camEnabled ? "Switch Off" : "Switch On";
		}


		/*** Window management ***/

		private Rect _windowPosition = new Rect();
		private GUIStyle _windowStyle = null;

		private void OnDraw()
		{
			if (PauseMenu.isOpen || FlightResultsDialog.isDisplaying || MapView.MapIsEnabled)
				return;

			_windowPosition = GUILayout.Window (0, _windowPosition, OnWindow, "Kasselblad Viewfinder", _windowStyle);

			// Center it on startup.
			if (_windowPosition.x == 0 && _windowPosition.y == 0) {
				_windowPosition.x = Screen.width / 2 - _windowPosition.width / 2;
				_windowPosition.y = Screen.height / 2 - _windowPosition.height / 2;
			}
		}

		private void OnWindow(int windowid)
		{
			GUILayoutOption[] viewSize = {GUILayout.Width (320), GUILayout.Height (240) };

			// Perform window layout.
			GUILayout.BeginVertical ();

			if (_camEnabled) {
				Quaternion rot = this.transform.rotation * Quaternion.Euler(0,180,0);

				RenderCam(this.transform.position, this.transform.forward, rot);
				GUILayout.Box (_camTex, viewSize);
			} else {
				GUILayout.Box ("Camera Off", viewSize);
			}

			GUILayout.BeginHorizontal ();

			if (GUILayout.Button (Events ["toggleOnOff"].guiName))
				toggleOnOff ();

			GUILayout.EndHorizontal ();
			GUILayout.EndVertical ();

			// Makes the window draggable
			GUI.DragWindow ();
		}

		private void InitStyles()
		{
			_windowStyle = new GUIStyle (HighLogic.Skin.window);
		}
	}

}

