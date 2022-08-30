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
    
    
	public partial class Screen1
	{		
		void Screen1_Opened(System.Object sender, System.EventArgs e)
		{
			if (!AE.Initialized)
			{
				AE.Error += AE_Error;
				AE.Initialize();
				ErrorTextBox.Text = "Initialized";
			}
		}
		void AE_Error(Exception x)
		{
			Dispatcher.Invoke(()=>ErrorTextBox.Text += Environment.NewLine + x.Message);
		}
		
		void GetIPButton_Click(System.Object sender, System.EventArgs e)
		{
			ErrorTextBox.Text = "Getting IP Address...";
			AE.GetIpAddress(ip => Dispatcher.Invoke(() => 
			{
				AddressLabel.Text = ip;
				ErrorTextBox.Text += Environment.NewLine + "Done";
			}));
		}
	}
}