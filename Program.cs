using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
	partial class Program : MyGridProgram
	{
		// COPY FROM HERE ================================================================================

		// USER CUSTOMIZABLE SETTINGS START >>>
		private const string ORE_BLACKLIST = "Stone"; // Comma seperated values with capitalized first letters. Default = "Stone"

		private const bool DEPTH_SHADING = false;

		private const uint
			SCAN_RES = 100,
			CAST_DIST = 200,
			CAST_ANGLE_MAX = 90;

		private static readonly Color
			BlankColor = Color.Black,
			CobaltColor = Color.Blue,
			GoldColor = Color.Yellow,
			IceColor = Color.LightBlue,
			IronColor = Color.Pink,
			MagnesiumColor = Color.ForestGreen,
			NickelColor = Color.Orange,
			PlatinumColor = Color.DarkRed,
			SiliconColor = Color.DarkBlue,
			SilverColor = Color.Gray,
			StoneColor = Color.White,
			UraniumColor = Color.LightGreen;

		// USER CUSTOMIZABLE SETTINGS END <<<
		private OreType[][] _currScanningOreTypeMap, _storedOreTypeMap;
		private float[][] _currScanningMatrix, _storedScanMatrix; // Depth maps
		private ProgramMode _currentProgMode = ProgramMode.Idle;
		private IEnumerator<bool> _currScanSequence;
		private List<IEnumerator<bool>> _lcdPrintingQueues = new List<IEnumerator<bool>>();
		private bool _setupValid = false;

		private readonly Dictionary<OreType, Color> _oreColorCodingMap = new Dictionary<OreType, Color>()
		{
			{OreType.Null, BlankColor },
			{OreType.Cobalt, CobaltColor },
			{OreType.Gold, GoldColor },
			{OreType.Ice, IceColor },
			{OreType.Iron, IronColor },
			{OreType.Magnesium, MagnesiumColor },
			{OreType.Nickel, NickelColor },
			{OreType.Platinum, PlatinumColor },
			{OreType.Silicon, SiliconColor },
			{OreType.Silver, SilverColor },
			{OreType.Stone, StoneColor },
			{OreType.Uranium, UraniumColor },
		};
		private readonly List<IMyTextPanel> TextPanels = new List<IMyTextPanel>();
		private readonly IMyOreDetector OreDetector = null;
		private readonly UpdateFrequency ScanFreq = UpdateFrequency.Update10;
		private const string SCRIPTREQ_BLOCKGROUP_NAME = "ORELIDAR", SCAN_ARG = "SCAN";

		// Font mod will be required for this one
		public char ColorToChar(Vector3 rgb)
		{
			return (char)((uint)0x3000 + (((byte)rgb.X >> 3) << 10) + (((byte)rgb.Y >> 3) << 5) + ((byte)rgb.Z >> 3));
		}

		public Program()
		{
			// Initialize script
			Me.CustomName = $"PB - {SCRIPTREQ_BLOCKGROUP_NAME}";
			Runtime.UpdateFrequency = ScanFreq;

			// Block referencing
			IMyBlockGroup blockGroup = GridTerminalSystem.GetBlockGroupWithName(SCRIPTREQ_BLOCKGROUP_NAME);
			blockGroup.GetBlocksOfType(TextPanels);
			List<IMyOreDetector> oreDetectors = new List<IMyOreDetector>();
			blockGroup.GetBlocksOfType(oreDetectors);
			if (oreDetectors.Count > 0)
				OreDetector = oreDetectors.First();

			// Load previously stored matrix
			//_storedScanMatrix = MatrixSerialization<float>.Deserialize(Storage);

			// Validity check
			_setupValid = OreDetector != null && TextPanels?.Count > 0;
			if (_setupValid)
			{
				// Rename blocks too
				if (!OreDetector.CustomName.Contains(SCRIPTREQ_BLOCKGROUP_NAME))
					OreDetector.CustomName += $" - {SCRIPTREQ_BLOCKGROUP_NAME}";
				TextPanels.ForEach(tp => tp.CustomName += !tp.CustomName.Contains(SCRIPTREQ_BLOCKGROUP_NAME) ? $" - {SCRIPTREQ_BLOCKGROUP_NAME}" : "");

				// Setup LCD panel formatting
				foreach (var lcd in TextPanels)
				{
					lcd.Enabled = true;
					lcd.FontColor = Color.White;
					lcd.FontSize = 0.18F / SCAN_RES * 100;
					lcd.Font = "Mono Color";
					lcd.TextPadding = 0;
					lcd.Alignment = TextAlignment.LEFT;
					lcd.BackgroundColor = Color.Red;
					lcd.WriteText("INIT SUCCESS");
					lcd.ContentType = ContentType.TEXT_AND_IMAGE;

					//if (_storedScanMatrix != null)
					//	_lcdPrintingQueues.Add(PrintStoredMatrix(lcd));
					//else
					_lcdPrintingQueues.Add(ShowTestScreen(lcd));
				}

				// Setup ore detector
				OreDetector.Enabled = true;
				OreDetector.BroadcastUsingAntennas = true;
				OreDetector.SetValue("OreBlacklist", ORE_BLACKLIST);
			}
		}

		//public void Save()
		//{
		//if (_storedScanMatrix != null) // TODO additionally save oreType
		//	Storage = MatrixSerialization<float>.Serialize(_storedScanMatrix);
		//}

		// Called by updater OR terminal (w/ argument for begin)
		public void Main(string argument)
		{
			if (!_setupValid)
			{
				Echo("Invalid setup: missing block or group.");
				return;
			}

			// Determine and utilize program state
			_currentProgMode = DetermineProgMode();
			switch (_currentProgMode)
			{
				// Screen printing takes priority if no argument given or not scanning
				case ProgramMode.Printing:
					IEnumerator<bool> currEnum = _lcdPrintingQueues[0];
					currEnum.MoveNext();
					if (currEnum.Current) // If finished, remove
					{
						_lcdPrintingQueues.RemoveAt(0);
						currEnum.Dispose();
					}
					return;

				// Step through another scan step, echo progress
				case ProgramMode.Scanning:
					_currScanSequence.MoveNext();
					if (_currScanSequence.Current) // If done, reset and queue screen prints
					{
						_currScanSequence = null;
						_storedScanMatrix = _currScanningMatrix; // Move to storage
						_storedOreTypeMap = _currScanningOreTypeMap;
						_lcdPrintingQueues.Clear();
						foreach (IMyTextPanel lcd in TextPanels)
							_lcdPrintingQueues.Add(PrintStoredMatrix(lcd));
					}
					return;

				// If idle, then listen for the argument to begin scanning
				default:
					if (argument == SCAN_ARG)
					{
						// Re-assign and initialize
						_storedScanMatrix = _currScanningMatrix;
						_storedOreTypeMap = _currScanningOreTypeMap;
						_currScanningMatrix = new float[SCAN_RES][];
						_currScanningOreTypeMap = new OreType[SCAN_RES][];
						for (int i = 0; i < SCAN_RES; i++)
						{
							_currScanningMatrix[i] = new float[SCAN_RES];
							_currScanningOreTypeMap[i] = new OreType[SCAN_RES];
						}

						_currScanSequence = ScanSequence();
					}
					return;
			}
		}

		private float Remap(float curr, float min, float max, float nextMin, float nextMax) =>
			(curr - min) / (max - min) * (nextMax - nextMin) + nextMin; // TODO add this to ZUtilLib

		private IEnumerator<bool> ScanSequence()
		{
			for (int y = 0; y < SCAN_RES; y++)
			{
				float currVertAngle = Remap(y, 0, SCAN_RES - 1, -CAST_ANGLE_MAX, CAST_ANGLE_MAX);

				for (int x = 0; x < SCAN_RES; x++)
				{
					// Input destination position using angle, curr pos, and the local orientation
					float currHorizAngle = Remap(x, 0, SCAN_RES - 1, -CAST_ANGLE_MAX, CAST_ANGLE_MAX);
					Vector3D currPos = OreDetector.GetPosition();
					Vector3D castDir = Vector3D.Rotate(OreDetector.WorldMatrix.Forward, MatrixD.CreateRotationY(MathHelper.ToRadians(currVertAngle)) + MatrixD.CreateRotationZ(MathHelper.ToRadians(currHorizAngle)));
					OreDetector.SetValue("RaycastTarget", currPos + (castDir.Normalized() * CAST_DIST));

					// Get and store result
					var result = OreDetector.GetValue<MyDetectedEntityInfo>("RaycastResult");
					if (result.IsEmpty() || result.Name?.Length == 0) // If nothing, then store empty and cont.
					{
						_currScanningMatrix[x][y] = 1;
						_currScanningOreTypeMap[x][y] = OreType.Null;
					}
					else // Otherwise store info
					{
						_currScanningMatrix[x][y] = (float)Vector3D.Distance(result.HitPosition ?? OreDetector.GetPosition(), currPos) / CAST_DIST;
						_currScanningOreTypeMap[x][y] = (OreType)Enum.Parse(typeof(OreType), result.Name);
					}
				}

				if (y % 20 == 0) // Yield frequency 
				{
					Echo($"Scanning progress: {y}/{SCAN_RES}");
					yield return false;
				}
			}
			yield return true;
		}

		private IEnumerator<bool> PrintStoredMatrix(IMyTextPanel lcd)
		{
			lcd.WriteText("");
			string outputPrint = "";
			for (int y = 0; y < SCAN_RES; y++)
			{
				for (int x = 0; x < SCAN_RES; x++)
				{
					float depthPercent = _storedScanMatrix[x][y];
					Color pixelColor = _oreColorCodingMap[_storedOreTypeMap[x][y]];
					outputPrint += ColorToChar(255 * (DEPTH_SHADING ? depthPercent : 1) * (Vector3)pixelColor);
				}
				outputPrint += "\n";

				if (y % 20 == 0)
				{
					lcd.WriteText(outputPrint);
					yield return false;
				}
			}
			lcd.WriteText(outputPrint);
			yield return true;
		}

		private IEnumerator<bool> ShowTestScreen(IMyTextPanel lcd)
		{
			string output = "";
			Random rand = new Random();
			for (int y = 0; y < SCAN_RES; y++)
			{
				for (int x = 0; x < SCAN_RES; x++)
					output += ColorToChar(new Vector3(rand.Next(1, 256), rand.Next(1, 256), rand.Next(1, 256)));
				output += "\n";

				if (y % 10 == 0)
				{
					lcd.WriteText(output);
					yield return false;
				}
			}
			lcd.WriteText(output);
			yield return true;
		}

		private enum ProgramMode
		{
			Idle, Printing, Scanning
		}

		private ProgramMode DetermineProgMode()
		{
			if (_lcdPrintingQueues.Count > 0)
				return ProgramMode.Printing;
			if (_currScanSequence != null)
				return ProgramMode.Scanning;
			return ProgramMode.Idle;
		}

		private enum OreType
		{
			Cobalt, Gold, Ice, Iron, Magnesium, Nickel, Platinum, Silicon, Silver, Uranium, Stone, Null
		}

		//public static class MatrixSerialization<T>
		//{
		//	const char ARR_SEPERATOR = ';', VAL_SEPERATOR = ',';
		//	public static string Serialize(T[][] matrix)
		//	{
		//		if (matrix.Length != matrix[0].Length || matrix.Length == 0)
		//			return null;

		//		string output = "";
		//		int sideLength = matrix.Length;
		//		for (int y = 0; y < sideLength; y++)
		//		{
		//			for (int x = 0; x < sideLength; x++)
		//				output += matrix[x][y].ToString() + VAL_SEPERATOR;
		//			output += ARR_SEPERATOR;
		//		}

		//		return output;
		//	}
		//	public static T[][] Deserialize(string matrixString)
		//	{
		//		// values is string[y][x]
		//		if (matrixString == null || matrixString.Length < 4)
		//			return null;
		//		string[][] valueMatrix = matrixString.Split(ARR_SEPERATOR).Where(s => s != "").Select(a => a.Split(VAL_SEPERATOR).Where(s => s != "").ToArray()).ToArray();
		//		if (valueMatrix.Length != valueMatrix[0].Length || valueMatrix.Length == 0)
		//			return null;

		//		int sideLength = valueMatrix.Length;
		//		T[][] outMatrix = new T[sideLength][];
		//		for (int i = 0; i < sideLength; i++)
		//			outMatrix[i] = new T[sideLength];

		//		for (int y = 0; y < sideLength; y++)
		//			for (int x = 0; x < sideLength; x++)
		//				outMatrix[x][y] = (T)Convert.ChangeType(valueMatrix[y][x], typeof(T));

		//		return outMatrix;
		//	}
		//}

		// COPY TO HERE ======================================================================================
	}
}
