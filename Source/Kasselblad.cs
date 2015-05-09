using UnityEngine;
using System;
using KSP.IO;
using System.Collections.Generic;
using System.Linq;

namespace Kasselblad
{
	public class Kasselblad : PartModule
	{

		#region Settings
		[KSPField]
		public Vector3 cameraPos = new Vector3(0,0,0);

		[KSPField]
		public Vector3 cameraRot = new Vector3(0,0,0);

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Field of View"), UI_FloatRange(minValue = 10.0f, maxValue = 120.0f, stepIncrement = 10.0f)]
		public float camFoV = 60.0f;

		
		[KSPField]
		public Vector2 cameraRes = new Vector2(320,240);


		[KSPField(isPersistant = false)]
		public float consumeRate = 0.2f;


		public bool _camSwitchedOn = true;
		public bool _hasPower = true;

		#endregion
		#region Events

		[KSPEvent(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 20.0f, guiActiveEditor = false, guiName = "Switch On")]
		public void toggleOnOff()
		{
			_camSwitchedOn = !_camSwitchedOn;
			UpdateCameraState ();

			Events["toggleOnOff"].guiName = _camSwitchedOn ? "Switch Off" : "Switch On";
		}
		


		#endregion
		#region Common Methods

		public override void OnStart (StartState state)
		{
			if (state != StartState.Editor) {
				
				// Set up the camera.
				InitRTT();

				// Initialize the action state
				Events["toggleOnOff"].guiName = _camSwitchedOn ? "Switch Off" : "Switch On";

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

				if (_camSwitchedOn)
				{
					double requested = consumeRate * TimeWarp.deltaTime;
					double granted = part.RequestResource("ElectricCharge", requested);

					print ("Requested: " + requested + ", granted: " + granted);

					_hasPower = (granted > requested / 2.0f);
					UpdateCameraState();

				}

		}

		#endregion
		#region Science

		private static string[] ScienceNames = {"MunarIndustriesKasselblad_1", "MunarIndustriesKasselblad_2", "MunarIndustriesKasselblad_3"};
		private static string[] DataNames = {"Black and White Photograph", "Color TV Capture", "High Res Photo"};

		
		private GUIStyle ScienceStyle;
		private GUISkin SkinStored;
		private GUIStyleState StyleDefault;


		// Takes a picture and generates a science report.
		public void DoScience ()
		{
			var experiment = ResearchAndDevelopment.GetExperiment (ScienceNames[(int)_camStyle]);
			var body = this.vessel.mainBody;
			var biome = ScienceUtil.GetExperimentBiome (body, this.vessel.latitude, this.vessel.longitude);
			var situation = ScienceUtil.GetExperimentSituation (this.vessel);
			var subject = ResearchAndDevelopment.GetExperimentSubject (experiment, situation, body, biome);

			float existingScience = ResearchAndDevelopment.GetScienceValue(experiment.baseValue * experiment.dataScale, subject);
			print ("Existing science for " + subject.title + " = " + existingScience);

			int points = 10;

			var data = new ScienceData(points, 1f, 0f, subject.id, DataNames[(int)_camStyle]);
			StoredData.Add(data);

			Texture2D photo = new Texture2D (_camTex.width, _camTex.height);
			RenderTexture old = RenderTexture.active;
			RenderTexture.active = _camTex;
			photo.ReadPixels(new Rect(0, 0, _camTex.width, _camTex.height), 0, 0);
			photo.Apply();
			RenderTexture.active = old;

			ReviewData(data, photo);
		}

		private List<ScienceData> StoredData = new List<ScienceData>();
		private void ReviewData(ScienceData Data, Texture2D Screenshot) 
		{
			StartCoroutine(ReviewDataCoroutine(Data, Screenshot));
		}

		public System.Collections.IEnumerator ReviewDataCoroutine(ScienceData Data, Texture2D Screenshot)
		{
			yield return new WaitForEndOfFrame();

			ExperimentResultDialogPage page = new ExperimentResultDialogPage
				(
					FlightGlobals.ActiveVessel.rootPart,    //hosting part
					Data,                                   //Science data
					Data.transmitValue,                     //scalar for transmitting the data
					Data.labBoost,                          //scalar for lab bonuses
					false,                                  //bool for show transmit warning
					"",                                     //string for transmit warning
					false,                                  //show the reset button
					false,                                  //show the lab option
					new Callback<ScienceData>(_onPageDiscard), 
					new Callback<ScienceData>(_onPageKeep), 
					new Callback<ScienceData>(_onPageTransmit), 
					new Callback<ScienceData>(_onPageSendToLab)
					);
			
			//page.scienceValue = 0f;
			ExperimentsResultDialog ScienceDialog = ExperimentsResultDialog.DisplayResult(page);

			//Store the old dialog gui information
			GUIStyle style = ScienceDialog.guiSkin.box;
			StyleDefault = style.normal;
			SkinStored = ScienceDialog.guiSkin;
			
			////Lets put a pretty picture on the science dialog.
			ScienceStyle = ScienceDialog.guiSkin.box;
			ScienceStyle.normal.background = Screenshot;
			
			ScienceStyle.fixedWidth = 240f * cameraRes.x / cameraRes.y;
			ScienceStyle.fixedHeight = 240f;

			ScienceDialog.guiSkin.window.fixedWidth = ScienceStyle.fixedWidth + 75;
		}

		private void _onPageDiscard(ScienceData Data)
		{
			StoredData.Remove(Data);
			ResetExperimentGUI();
			return;
		}

		private void _onPageKeep(ScienceData Data)
		{
			StoredData.Add(Data);
			ResetExperimentGUI();
			return;
		}

		private void _onPageTransmit(ScienceData Data)
		{
			//Grab list of available antenneas
			List<IScienceDataTransmitter> AvailableTransmitters = vessel.FindPartModulesImplementing<IScienceDataTransmitter>();
			
			if (AvailableTransmitters.Count() > 0)
			{
				AvailableTransmitters.First().TransmitData(new List<ScienceData>{ Data });
			}
			
			ResetExperimentGUI();
		}

		private void _onPageSendToLab(ScienceData Data)
		{
			ResetExperimentGUI();
			return;
		}
		private void ResetExperimentGUI()
		{
			//print("Resetting GUI...");
			if (SkinStored != null)
			{
				SkinStored.box.normal = StyleDefault;
				SkinStored.box.fixedWidth = 0f;
				SkinStored.box.fixedHeight = 0f;
				SkinStored.window.fixedWidth = 400f;
			}
			
			ScienceStyle.fixedHeight = 0f;
			ScienceStyle.fixedWidth = 0f;
		}

		#endregion
		#region Persistence 

		public override void OnSave (ConfigNode node)
		{
			PluginConfiguration config = PluginConfiguration.CreateForType<Kasselblad>();
			config.SetValue ("Window Position", _windowPosition);
			config.SetValue ("Camera Enabled", _camSwitchedOn);

			config.save ();	
		}
		
		public override void OnLoad (ConfigNode node)
		{
			PluginConfiguration config = PluginConfiguration.CreateForType<Kasselblad>();
			config.load ();

			// Intentionally ignoring the width and height here.
			Rect oldRect = config.GetValue<Rect> ("Window Position");
			_windowPosition.x = oldRect.x;
			_windowPosition.y = oldRect.y;

			_camSwitchedOn = config.GetValue<bool> ("Camera Enabled");
			UpdateCameraState ();
		}

		#endregion
		#region Camera Management

		public enum CamStyle {
			BlackAndWhite,
			AnalogColor,
			DigitalColor
		};

		private CamStyle _camStyle;

		public void SetStyle(Kasselblad.CamStyle style) {
			_camStyle = style;
			switch (style) {
			case CamStyle.BlackAndWhite:
				_movietimeFilter = new CameraFilterBlackAndWhiteLoResTV ();
				_standbyTex = GameDatabase.Instance.GetTexture ("MunarIndustries/textures/TestPattern_NTSC", false);
				break;
			case CamStyle.AnalogColor:
				_movietimeFilter = new CameraFilterColorLoResTV ();
				_standbyTex = GameDatabase.Instance.GetTexture ("MunarIndustries/textures/ColorBars_NTSC", false);
				break;
			case CamStyle.DigitalColor:
				_movietimeFilter = new CameraFilterNormal ();
				_standbyTex = GameDatabase.Instance.GetTexture ("MunarIndustries/textures/ColorBars_NTSC", false);
				break;
			}
			
			_movietimeFilter.Activate (); // Is this necessary? who knows!
		}

		private Texture overlay;
		private CameraFilter _movietimeFilter;
		private RenderTexture _camTex;
		private Texture _standbyTex;
		private GameObject[] _camobjs = new GameObject[GameCameras.Length];
		private Camera[] _cams = new Camera[GameCameras.Length];

		private bool isCamEnabled() {
			return _camSwitchedOn && _hasPower;
		}

		private static string[] GameCameras = {
			"GalaxyCamera", "Camera ScaledSpace", "Camera 01", "Camera 00", "Camera VE Underlay", "Camera VE Overlay"
		};

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

		private void InitRTT() {
			_camTex = new RenderTexture (cameraRes.x, cameraRes.y, 1, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB);
			_camTex.isPowerOfTwo = false;
			_camTex.Create ();

			SetupMaterials ();
			CameraFilter.InitializeAssets();
			SetStyle (CamStyle.BlackAndWhite);

			for (int i = 0; i < GameCameras.Length; i++) SetupCamera (i);
		}

		private void SetupMaterials() {
			overlay = GameDatabase.Instance.GetTexture ("MunarIndustries/textures/dockingcam", false);
		}

		private void UpdateCameraState() {	
			for (int i = 0; i < 3; i++)
				if (_cams[i] != null) _cams [i].enabled = isCamEnabled();
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
			_cams [i].enabled = isCamEnabled();
		}

		virtual public void RenderCam(Vector3 pos, Vector3 aim, Quaternion rot) {
			RenderTexture currentRT = RenderTexture.active;
			RenderTexture.active = _camTex;

			if (isCamEnabled ()) {

				for (int i = 0; i < _cams.Length; i++) {
					Camera cam = _cams [i];
					if (cam == null)
						continue;

					if (i == 2 || i == 3) {
						cam.transform.position = pos;
					}

					cam.transform.forward = aim;
					cam.transform.rotation = rot;

					cam.fieldOfView = camFoV;
					cam.targetTexture = _camTex;

					cam.Render ();
				}

				Graphics.Blit (_camTex, _camTex, dealphaMaterial);
				
				// DrawTexture is in world space, so we have to make an ortho matrix.
				GL.PushMatrix();
				GL.LoadOrtho();
				Graphics.DrawTexture(new Rect(0, 0, 1, 1), overlay);
				GL.PopMatrix();

			} else {
				Graphics.Blit (_standbyTex, _camTex);
			}

			// Now modify the texture to match our chosen style.
			if (_movietimeFilter != null) {
				_movietimeFilter.RenderImageWithFilter(_camTex, _camTex);
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

		#endregion
		#region GUI

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
			GUILayoutOption[] viewSize = {GUILayout.Width (cameraRes.x), GUILayout.Height (cameraRes.y) };

			// Perform window layout.
			GUILayout.BeginVertical ();

				Quaternion rot = this.transform.rotation * Quaternion.Euler(cameraRot);

				RenderCam(this.transform.position + this.transform.rotation * cameraPos, this.transform.forward, rot);
				GUILayout.Box (_camTex, viewSize);


			var values = Enum.GetValues (typeof(Kasselblad.CamStyle));

			foreach (Kasselblad.CamStyle value in values) {
				if (GUILayout.Button (value.ToString ()))
					SetStyle (value);
			}

			if (!_camSwitchedOn) {
				GUILayout.Label ("Camera Switched Off.");
			} else if (!_hasPower) {
				GUILayout.Label ("Insufficient electric charge!");
			} else {
				if (GUILayout.Button ("Take Science Photo"))
					DoScience ();
			}

			GUILayout.EndVertical ();

			// Makes the window draggable
			GUI.DragWindow ();
		}

		#endregion
	}

}

