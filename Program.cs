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
		// COPY FROM HERE ======================================================================================

		// USER CUSTOMIZABLE SETTINGS START >>>
		private const uint
			LCD_RES = 200;

		// USER CUSTOMIZABLE SETTINGS END <<<

		private ProgramMode _currentProgMode = ProgramMode.Idle;
		private IEnumerator<bool> _currScanSequence;
		private List<IEnumerator<bool>> _lcdPrintingQueues = new List<IEnumerator<bool>>();
		private bool _setupValid = false;
		private readonly List<IMyTextPanel> _textPanels = new List<IMyTextPanel>();
		private readonly IMyOreDetector _oreDetector = null;
		private readonly UpdateFrequency _scanFreq = UpdateFrequency.Update10;
		private const string SCRIPTREQ_BLOCKGROUP_NAME = "ORELIDAR", SCAN_ARG = "SCAN";

		// Font mod will be required for this one
		public char ColorToChar(Vector3 rgb)
		{
			return (char)((uint)0x3000 + (((byte)rgb.X >> 3) << 10) + (((byte)rgb.Y >> 3) << 5) + ((byte)rgb.Z >> 3));
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

		public Program()
		{
			// Initialize script
			Me.CustomName = $"PB - {SCRIPTREQ_BLOCKGROUP_NAME}";
			Runtime.UpdateFrequency = _scanFreq;

			// Block referencing
			IMyBlockGroup blockGroup = GridTerminalSystem.GetBlockGroupWithName(SCRIPTREQ_BLOCKGROUP_NAME);
			blockGroup.GetBlocksOfType(_textPanels);
			List<IMyOreDetector> oreDetectors = new List<IMyOreDetector>();
			blockGroup.GetBlocksOfType(oreDetectors);
			if (oreDetectors.Count > 0)
				_oreDetector = oreDetectors.First();

			// Validity check
			_setupValid = _oreDetector != null && _textPanels?.Count > 0;
			if (_setupValid)
			{
				// Rename blocks too
				if (!_oreDetector.CustomName.Contains(SCRIPTREQ_BLOCKGROUP_NAME))
					_oreDetector.CustomName += $" - {SCRIPTREQ_BLOCKGROUP_NAME}";
				_textPanels.ForEach(tp => tp.CustomName += !tp.CustomName.Contains(SCRIPTREQ_BLOCKGROUP_NAME) ? $" - {SCRIPTREQ_BLOCKGROUP_NAME}" : "");

				// Setup LCD panel formatting
				foreach (var lcd in _textPanels)
				{
					lcd.Enabled = true;
					lcd.FontColor = Color.White;
					lcd.FontSize = 0.18F / LCD_RES * 100;
					lcd.Font = "Mono Color";
					lcd.TextPadding = 0;
					lcd.Alignment = TextAlignment.LEFT;
					lcd.BackgroundColor = Color.Red;
					lcd.WriteText("INIT SUCCESS");
					lcd.ContentType = ContentType.TEXT_AND_IMAGE;

					_lcdPrintingQueues.Add(ShowTestScreen(lcd));
				}

				// Setup ore detector
				_oreDetector.Enabled = true;
				_oreDetector.BroadcastUsingAntennas = true;
			}
		}

		public void Save()
		{
			// Called when the program needs to save its state. Use
			// this method to save your state to the Storage field
			// or some other means. 
		}

		// Called by updater OR terminal (w/ argument for begin)
		public void Main(string argument)
		{
			if (!_setupValid)
			{
				Echo("Invalid setup: missing block or group.");
				return;
			}

			_currentProgMode = DetermineProgMode();

			// Screen printing takes priority if no argument given
			switch (_currentProgMode)
			{
				case ProgramMode.Printing:
					IEnumerator<bool> currEnum = _lcdPrintingQueues[0];
					currEnum.MoveNext();
					if (currEnum.Current) // If finished, remove
					{
						_lcdPrintingQueues.RemoveAt(0);
						currEnum.Dispose();
					}
					return;

				case ProgramMode.Scanning:
					// TODO is scanning operation
					return;

				default: // If idle, then listen for the argument to begin scanning
					// CONTINUE HERE with idle listening
					return;
			}
		}

		private IEnumerator<bool> ScanSequence()
		{
			yield return false;

			yield return true;
		}

		private IEnumerator<bool> ShowTestScreen(IMyTextPanel lcd)
		{
			string output = "";
			Random rand = new Random();
			for (int y = 0; y < LCD_RES; y++)
			{
				for (int x = 0; x < LCD_RES; x++)
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

		// COPY TO HERE ======================================================================================
	}
}
