using Gtk;
using Gdk;
using GLib;
using System;

namespace WordCloud
{
	class MainClass
	{
		public static void Main (string[] args)
		{
			if (GLib.Thread.Supported)
				GLib.Thread.Init (); // secure GLib
			else {
				Console.WriteLine ("Error: This information requires GLib thread support to be executed properly");
				return;
			}
			Gdk.Threads.Init (); // secure Gdk
			
			Application.Init (); 
			MainWindow win = new MainWindow ("Word Cloud"); //Create the Window
			win.Title = "Word Cloud";
			
			win.ShowAll ();
			Application.Run ();
		}
	}
}
