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
	using System.Net.Sockets;
	using Core.Api.DataSource;
	using System.ComponentModel;
	using System.Threading.Tasks;

	public partial class AE
	{
		public const string IP = "192.168.254.30";
		public const int PORT = 502;
		public const byte SideA = 1;
		public const byte SideB = 16;
		static TcpClient tcp;
		static UInt16 NextTransactionId = 0;
		public static bool Initialized = false;

		public delegate void ErrorEventHandler(Exception x);
		public static event ErrorEventHandler Error;

		//Initialize
		public static void Initialize()
		{
			if (!Initialized)
			{
				Globals.Tags.SetCurrentModeA.ValueChange += (s, e) => { if (Globals.Tags.SetCurrentModeA.Value.Bool == true) SetRegulationMode(SideA, RegulationMode.Current); };
				Globals.Tags.SetCurrentModeB.ValueChange += (s, e) => { if (Globals.Tags.SetCurrentModeB.Value.Bool == true) SetRegulationMode(SideB, RegulationMode.Current); };
				Globals.Tags.EnableA.ValueChange += (s, e) => SetEnable(SideA, Globals.Tags.EnableA.Value.Bool);
				Globals.Tags.EnableB.ValueChange += (s, e) => SetEnable(SideB, Globals.Tags.EnableB.Value.Bool);
				Globals.Tags.CurrentA.ValueChange += (s, e) => SetSetpoint(SideA, Globals.Tags.CurrentA.Value.UInt);
				Globals.Tags.CurrentB.ValueChange += (s, e) => SetSetpoint(SideB, Globals.Tags.CurrentB.Value.UInt);
				Initialized = true;
			}
			Handle(new Exception("Initialized"));
		}

		public static void SetRegulationMode(int side, RegulationMode mode)
		{
			SendCommand(side, Command.RegulationMode, new byte[] { (byte)mode });
		}
		public static void SetEnable(int side, bool enable)
		{
			SendCommand(side, Command.SideEnable, new byte[] { enable ? (byte)1 : (byte)0 });
		}
		public static void SetSetpoint(int side, UInt32 current)
		{
			SendCommand(side, Command.Setpoint32, new byte[] { (byte)current, (byte)(current >> 8), (byte)(current >> 16), (byte)(current >> 24) });
		}
		public static void GetIpAddress(Action<string> callback)
		{
			SendCommand(0, Command.ReportSystemControl, new byte[] { (byte)ReportSubCommand.IpAddress }, responseData =>
				{
				if (responseData == null)
				{
					callback(string.Empty);
					return;
				}
				if (responseData.Length != 4)
				{
					Handle(new Exception("Wrong data length: " + responseData.Length));
					callback(string.Empty);
					return;
				}
				callback(responseData[3] + "." + responseData[2] + "." + responseData[1] + "." + responseData[0]);
				});
		}
		private static byte[] SendTCP(byte[] packet)
		{
			if (tcp == null)
				tcp = new TcpClient(IP, PORT);

			var stream = tcp.GetStream();
			stream.Write(packet, 0, packet.Length);
			byte[] buffer = new byte[256];
			byte[] response = new byte[stream.Read(buffer, 0, buffer.Length)];

			for (int i = 0; i < response.Length; i++)
				response[i] = buffer[i];

			return response;
		}
		private static void SendCommand(int side, Command command, byte[] data, Action<byte[]> callback = null)
		{
			new Task(() =>
				{
				try
				{
					if (!(side == SideA || side == SideB))
						throw new ArgumentOutOfRangeException("side", "Side must be " + SideA + " or " + SideB);

					//construct modbus packet to contain AE command (see page 4-35)
					byte[] packet = new byte[data.Length + 12];

					var transactionId = NextTransactionId++;
					//transaction ID
					packet[0] = (byte)(transactionId / 0x100);
					packet[1] = (byte)(transactionId % 0x100);
					//Protocol ID (always 0?)
					packet[2] = 0;
					packet[3] = 0;
					//Number of Bytes
					packet[4] = (byte)((data.Length + 6) / 0x100);
					packet[5] = (byte)((data.Length + 6) % 0x100);
					//Unit Id (side)
					packet[6] = (byte)side;
					//Function Code (always 100)
					packet[7] = 100;
					//ENDIANNESS CHANGES!
					//AE Command Number
					packet[8] = (byte)command;
					//CSR (Padding)
					packet[9] = 0;
					//Data Length
					packet[10] = (byte)(data.Length % 0x100);
					packet[11] = (byte)(data.Length / 0x100);
					//Data
					int i = 12;
					foreach (byte b in data)
						packet[i++] = b;

					var response = SendTCP(packet);

					//Check Packet Validity
					if (response.Length < 9)
						throw new Exception("Bad Response: Too short");
					if (response[0] != transactionId / 0x100 || response[1] != transactionId % 0x100)
						throw new Exception("Bad Response: Wrong transactionId");
					if (response[4] * 0x100 + response[5] != response.Length - 6)
						throw new Exception("Bad Response: Packet claimed " + (response[4] * 0x100 + response[5]) + " bytes but contained " + (response.Length - 6) + " bytes");

					//Error Packet
					if (response[7] == 228)
						throw new Exception("Error Code Returned: " + response[8]);
					//Check for function code 100
					if (response[7] != 100)
						throw new Exception("Bad Response: Unexpected Function Code: " + response[7]);
					//Check CSR code
					if (response[9] != 0)
						throw new Exception("Error: CSR " + response[9]);
					//Check Data Length
					var dataLength = response[10] + response[11] * 0x100;
					if (dataLength != response.Length - 12)
						throw new Exception("Bad Response: Data length " + dataLength + " does not equal data received " + (response.Length - 12));

					//Get response data
					var responseData = new byte[dataLength];
					for (i = 0; i < dataLength; i++)
						responseData[i] = response[i + 12];
					if (callback != null)
						callback(responseData);
				}
				catch (Exception x)
				{
					Handle(x);
					if (callback != null)
						callback(null);
				}
				}).Start();
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