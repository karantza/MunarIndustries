using UnityEngine;
using System;

using KSP.IO;

namespace Kasselblad
{
	public class Kasselblad : PartModule
	{
		/*** Settings ***/

		
		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "FoV"),
		 UI_FloatRange(minValue = 30.0f, maxValue = 90.0f, stepIncrement = 5.0f)]
		public float camFoV = 60.0f;

		public enum CamStyle {
			LazorDP,

			MT_BnW0,
			MT_BnW1,
			MT_BnW2,
			
			MT_COLOR0,
			MT_COLOR1,
			MT_COLOR2,
			
			MT_SEPIA,
			MT_NORMAL,
			MT_THERMAL,
			MT_NIGHTVISION,

		};

		/*** Events ***/

		[KSPEvent(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 20.0f, guiActiveEditor = false, guiName = "Switch On")]
		public void toggleOnOff()
		{
			_camEnabled = !_camEnabled;
			
			for (int i = 0; i < 3; i++)
				_cams [i].enabled = _camEnabled;
			
			Events["toggleOnOff"].guiName = _camEnabled ? "Switch Off" : "Switch On";
		}
		


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


		// Other materials needed
		
		private Material grayscaleMaterial;
		private Texture overlay;

		private CameraFilter _movietimeFilter;

		private CamStyle _style;

		public void SetStyle(Kasselblad.CamStyle style) {
			_style = style;

			switch (style) {
			case CamStyle.LazorDP:
				_movietimeFilter = null;
				break;
			case CamStyle.MT_BnW0:
				_movietimeFilter = new CameraFilterBlackAndWhiteFilm ();
				break;
			case CamStyle.MT_BnW1:
				_movietimeFilter = new CameraFilterBlackAndWhiteLoResTV ();
				break;
			case CamStyle.MT_BnW2:
				_movietimeFilter = new CameraFilterBlackAndWhiteHiResTV ();
				break;
			case CamStyle.MT_COLOR0:
				_movietimeFilter = new CameraFilterColorFilm ();
				break;
			case CamStyle.MT_COLOR1:
				_movietimeFilter = new CameraFilterColorLoResTV ();
				break;
			case CamStyle.MT_COLOR2:
				_movietimeFilter = new CameraFilterColorHiResTV ();
				break;
			case CamStyle.MT_NIGHTVISION:
				_movietimeFilter = new CameraFilterNightVision ();
				break;
			case CamStyle.MT_NORMAL:
				_movietimeFilter = new CameraFilterNormal ();
				break;
			case CamStyle.MT_SEPIA:
				_movietimeFilter = new CameraFilterSepiaFilm ();
				break;
			case CamStyle.MT_THERMAL:
				_movietimeFilter = new CameraFilterThermal ();
				break;
			}
			if (_movietimeFilter != null) {
				_movietimeFilter.Activate (); // Is this necessary? who knows!
			}
		}

		private void InitRTT() {
			_camTex = new RenderTexture (1024, 768, 1, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB);
			_camTex.isPowerOfTwo = false;
			_camTex.Create ();

			SetupMaterials ();
				CameraFilter.InitializeAssets();

			for (int i = 0; i < GameCameras.Length; i++) SetupCamera (i);
		}

		private void SetupMaterials() {
			grayscaleMaterial = new Material (Shader.Find("Hidden/Grayscale Effect"));
			Texture ramp = GameDatabase.Instance.GetTexture ("MunarIndustries/textures/ramp_grayscale", false);
			grayscaleMaterial.SetTexture("_RampTex", ramp);
			grayscaleMaterial.SetFloat("_RampOffset", 0);

			overlay = GameDatabase.Instance.GetTexture ("MunarIndustries/textures/dockingcam", false);

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

			// Now modify the texture to match our chosen style.
			switch (_style) {
			case CamStyle.LazorDP:
				
				Graphics.Blit(_camTex, _camTex, grayscaleMaterial);

				// DrawTexture is in world space, so we have to make an ortho matrix.
				GL.PushMatrix();
				GL.LoadOrtho();
				Graphics.DrawTexture(new Rect(0, 0, 1, 1), overlay);
				GL.PopMatrix();
				break;

			default:
				if (_movietimeFilter != null) {
					_movietimeFilter.RenderImageWithFilter(_camTex, _camTex);
				}
				break;
			}


			RenderTexture.active = currentRT;
		}

		private Camera FindCamera(string name) {
			foreach(Camera camera in Camera.allCameras) {
				if(camera.name == name)
					return camera;
			}
			return null;
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

		private void InitStyles()
		{
			_windowStyle = new GUIStyle (HighLogic.Skin.window);
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


			var values = Enum.GetValues (typeof(Kasselblad.CamStyle));

			foreach (Kasselblad.CamStyle value in values) {
				if (GUILayout.Button (value.ToString ()))
					SetStyle (value);
			}

			if (GUILayout.Button (Events ["toggleOnOff"].guiName))
				toggleOnOff ();

			GUILayout.EndVertical ();

			// Makes the window draggable
			GUI.DragWindow ();
		}
	}

}

