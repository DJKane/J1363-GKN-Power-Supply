//--------------------------------------------------------------
// Press F1 to get help about using script.
// To access an object that is not located in the current class, start the call with Globals.
// When using events and timers be cautious not to generate memoryleaks,
// please see the help for more information.
//---------------------------------------------------------------

namespace Neo.ApplicationFramework.Generated
{
	using System.Windows.Forms;
	using System;
	using System.Drawing;
	using Neo.ApplicationFramework.Tools;
	using Neo.ApplicationFramework.Common.Graphics.Logic;
	using Neo.ApplicationFramework.Controls;
	using Neo.ApplicationFramework.Interfaces;
	using System.IO.Ports;
	using Core.Api.DataSource;
	using System.ComponentModel;
	using System.Diagnostics;

	public partial class AE
	{
		public const byte SideA = 1;
		public const byte SideB = 16;
		static SerialPort serial;
		public static TimeSpan Timeout = TimeSpan.FromSeconds(0.5);
		public static Stopwatch timer = new Stopwatch();
		
		public static bool Initialized = false;

		public delegate void ErrorEventHandler(Exception x);
		public static event ErrorEventHandler Error;

		public static void Initialize()
		{
			if (!Initialized)
			{
				InitializeSerial();
				Globals.Tags.SetCurrentModeA.ValueChange += (s, e) => { if (Globals.Tags.SetCurrentModeA.Value.Bool == true) SetRegulationMode(SideA, RegulationMode.Current); };
				Globals.Tags.SetCurrentModeB.ValueChange += (s, e) => { if (Globals.Tags.SetCurrentModeB.Value.Bool == true) SetRegulationMode(SideB, RegulationMode.Current); };
				Globals.Tags.EnableA.ValueChange += (s, e) => SetEnable(SideA, Globals.Tags.EnableA.Value.Bool);
				Globals.Tags.EnableB.ValueChange += (s, e) => SetEnable(SideB, Globals.Tags.EnableB.Value.Bool);
				Globals.Tags.SetSPA.ValueChange += (s, e) => { if (Globals.Tags.SetSPA.Value.Bool == true) SetSetpoint(SideA, Globals.Tags.CurrentA.Value.UInt); };
				Globals.Tags.SetSPB.ValueChange += (s, e) => { if (Globals.Tags.SetSPB.Value.Bool == true) SetSetpoint(SideB, Globals.Tags.CurrentB.Value.UInt); };
				Initialized = true;
			}
			Handle(new Exception("Initialized"));
		}
		private static void InitializeSerial()
		{
			serial = new SerialPort("COM1", 115200, Parity.Odd, 8, StopBits.One);
			serial.Open();
			serial.WriteTimeout =
			serial.ReadTimeout = (int)Timeout.TotalMilliseconds;
			serial.DataReceived += new SerialDataReceivedEventHandler(serial_DataReceived);
		}

		public void CheckTimeout()
		{
			if (Globals.Tags.Sending.Value.Bool == true && timer.Elapsed > Timeout)
			{
				Globals.Tags.Sending.Value = false;
				Handle(new Exception("Serial Timeout"));
				timer.Stop();
			}
		}

		public static void SetRegulationMode(byte side, RegulationMode mode)
		{
			SendCommand(side, Command.RegulationMode, new byte[] { (byte)mode });
		}
		public static void SetEnable(byte side, bool enable)
		{
			SendCommand(side, Command.SideEnable, new byte[] { enable ? (byte)1 : (byte)0 });
		}
		public static void SetSetpoint(byte side, UInt32 current)
		{
			SendCommand(side, Command.Setpoint32, new byte[] { (byte)current, (byte)(current >> 8), (byte)(current >> 16), (byte)(current >> 24) });
		}
		private static void SendCommand(byte side, Command command, byte[] data)
		{
			try
			{
				if (!(side == SideA || side == SideB))
					throw new ArgumentOutOfRangeException("side", "Side must be " + SideA + " or " + SideB);
				
				int i = 2;
				
				int length = data.Length + (data.Length >= 7 ? 3 : 2);
				byte[] packet = new byte[length + 1];
				
				//Length
				packet[0] = (byte)(side << 3);
				if (data.Length < 7)
					packet[0] += (byte)data.Length;
				else
				{
					packet[0] += 7;
					//use data byte if data is 7 or more bytes
					i = 3;
					packet[2] = (byte)data.Length;
				}
				
				//Command
				packet[1] = (byte)command;
				
				//Data
				foreach (byte b in data)
					packet[i++] = b;
				
				//Checksum
				byte checksum = 0;
				for (i = 0; i < length; i++)
					checksum ^= packet[i];
				packet[length] = checksum;
				
				//Send
				serial.Write(packet, 0, packet.Length);
				timer.Reset();
				timer.Start();
				
				Globals.Tags.Sending.Value = true;
			}
			catch (Exception x)
			{
				Handle(x);
			}
		}
		private	static void serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
		{
			try
			{
				Globals.Tags.Sending.Value = false;
				timer.Stop();
				
				int bytes = serial.BytesToRead;
				if (bytes < 4)
					throw new Exception("Bad Response: Too short");
				
				byte[] response = new byte[bytes];
				serial.Read(response, 0, bytes);
				
				//Checksum
				int checksum = 0;
				int i = 0;
				for(i = 0; i < bytes; i++)
				{
					checksum ^= response[i];
				}
				if (checksum != 0)
					throw new Exception("Bad Response: Checksum failed");
				
				//Get Length
				i = 2;
				int length = response[0] & 7;
				if (length == 7)
				{
					i = 3;
					length = response[2];
				}

				//Check Packet Validity
				if (length != bytes - i - 1)
					throw new Exception("Bad Response: Packet claimed " + length + " bytes but contained " + (bytes - i - 1) + " bytes");

				//Error Packet
				if (response[i] > 0)
					throw new Exception("Error Code Returned: " + response[i]);

				//Ignore response data
				//var responseData = new byte[length];
				//for (int j = 0; j < length; j++)
				//	responseData[j] = response[i + j];
			}
			catch(Exception x)
			{
				Handle(x);
			}
		}
		
		private static void Handle(Exception x)
		{
			if (Error != null)
				Error.Invoke(x);
		}

		public enum RegulationMode
		{
			Power = 6,
			Voltage = 7,
			Current = 8
		}
		public enum Command
		{
			Null = 0,
			OutputOff = 1,
			OutputOn = 2,
			/// <summary>6 = Power, 7 = Voltage, 8 = Current</summary>
			RegulationMode = 3,
			Joules = 4,
			JouleModeOnOff = 5,
			/// <summary>Setpoint for regulation method. Power is in watts (6000 for 6kW). Current is in centiamps (1500 for 15A). Voltage is in Volts (800 for 800V)</summary>
			Setpoint = 6,
			ArcAverageWindow = 7,
			ArcTimes = 8,
			ArcCountLimit = 9,
			ArcSenseVoltage = 10,
			ActiveTarget = 11,
			TargetLife = 12,
			TargetEnable = 13,
			/// <summary>2 = Host, 4 = User, 6 = Local</summary>
			ControlMode = 14,
			RampEnable = 15,
			RampTime = 16,
			ProgramSource = 17,
			SideEnable = 18,
			RecipeStepCount = 19,
			RecipeRegulationMode = 20,
			RecipeRampTime = 21,
			RecipeSetpoint = 22,
			RecipeRunTime = 23,
			JouleThreshold = 26,
			PredefinedTargetType = 30,
			OutOfSetpointTimer = 31,
			RampStartPoint = 32,
			GlobalOnOff = 37,
			ArcResetCounters = 38,
			CommWatchdogTimer = 39,
			CommPortTimeout = 40,
			ProcessVoltageLimitOnOff = 47,
			ProcessVoltageLimitLower = 48,
			UserPowerLimit = 49,
			UserVoltageLimit = 50,
			UserCurrentLimit = 51,
			UserIgnitionStrikeLimit = 52,
			ArcManagementResponseTime = 62,
			ArcManagementResponseMode = 63,
			RealTimeClock = 70,
			/// <summary>See SystemSubCommand</summary>
			SystemControl = 71,
			/// <summary>Setpoint for regulation method, but allows bigger values. Power is in watts (6000 for 6kW). Current is in centiamps (1500 for 15A). Voltage is in Volts (800 for 800V) </summary>
			Setpoint32 = 78,
			RecipeRampHoldResume = 100,
			RecipeRuntimeSetpoint = 101,
			MasterReset = 119,
			ResetToDefault = 126,
			ReportPsType = 128,
			ReportSupplySize = 129,
			ReportSetpointLimits = 130,
			ReportJouleThreshold = 136,
			ReportGlobalOnOff = 137,
			ReportCommWatchdogTimer = 139,
			ReportCommPortTimeout = 140,
			ReportUserPowerLimit = 141,
			ReportUserVoltageLimit = 142,
			ReportUserCurrentLimit = 143,
			ReportUserIgnitionStrikeLimit = 144,
			ReportRecipeRampHoldResume = 150,
			ReportRampStartPoint = 152,
			ReportJouleModeOnOff = 153,
			/// <summary>6 = Power, 7 = Voltage, 8 = Current</summary>
			ReportRegulationMode = 154,
			/// <summary>2 = Host, 4 = User, 6 = Local</summary>
			ReportControlMode = 155,
			ReportActiveTarget = 156,
			ReportTargetLife = 157,
			ReportRampTime = 158,
			ReportRampTimeRemaining = 159,
			/// <summary>0 = OK, 1 = Control mode invalid, 2 = Unit already on, 7 = Active fault exists, 11 = Bus not ready, 16 = End of target life event, 44 = No power on request yet</summary>
			ReportOutputStatus = 161,
			/// <summary>See Manual page 4-83 for bits</summary>
			ReportProcessStatus = 162,
			ReportConfigStatus = 163,
			/// <summary>Setpoint for regulation method. Power is in watts (6000 for 6kW). Current is in centiamps (1500 for 15A). Voltage is in Volts (800 for 800V)</summary>
			ReportSetpoint = 164,
			ReportActualPower = 165,
			ReportActualVoltage = 166,
			ReportActualCurrent = 167,
			ReportActualAll = 168,
			ReportSetpointAll = 169,
			ReportArcTimes = 170,
			ReportArcSenseVoltage = 171,
			ReportJoulesRemaining = 172,
			ReportJoules = 173,
			ReportPredefinedTargetType = 174,
			ReportOnTimeElapsed = 175,
			ReportProcessVoltageLimitLower = 177,
			ReportArcCountLimit = 178,
			ReportRecipeStatus = 179,
			ReportRecipeStepCount = 180,
			ReportRecipeRampTime = 181,
			ReportRecipeSetpoint = 182,
			ReportRecipeRunTime = 183,
			ReportOutOfSetpointTimeRemaining = 184,
			ReportOutOfSetpointTimer = 187,
			ReportArcDensity = 188,
			ReportMicroArcCount = 189,
			ReportArcCount = 190,
			ReportArcAverageWindow = 192,
			ReportSoftwareRevision = 198,
			/// <summary>See ReportSubCommand</summary>
			ReportSystemControl = 204,
			ReportRealTimeClock = 205,
			ReportActualAll32 = 208,
			ReportStats = 220,
			ReportPin = 221,
			ReportFaultCode = 223,
			ReportArcManagementResponseStatus = 225,
			ReportUnitInfo = 231,
			/// <summary>Setpoint for regulation method, but allows bigger values. Power is in watts (6000 for 6kW). Current is in centiamps (1500 for 15A). Voltage is in Volts (800 for 800V) </summary>
			ReportSetpoint32 = 238,
			ReportSnapshot = 248,
		}
		public enum SystemSubCommand
		{
			IpAddress = 0,
			Gateway = 1,
			SubnetMask = 2,
			DhcpEnable = 5,
			PowerLimit = 11,
			VoltageLimit = 12,
			CurrentLimit = 13,
			DomainName = 200,
			DnsIp = 202,
			DnsConfig = 203
		}
		public enum ReportSubCommand
		{
			IpAddress = 0,
			Gateway = 1,
			SubnetMask = 2,
			MacId = 3,
			InitStatus = 4,
			DhcpEnable = 5,
			DhcpBootpStatus = 8,
			OutputLimits = 11,
			UserLimits = 12,
			CurrentScaling = 27,
			JouleSetpointRange = 42,
			WarningFault = 92,
			CsrDescription = 97,
			DomainName = 200,
			DnsIp = 202,
			DnsConfig = 203
		}
	}
}