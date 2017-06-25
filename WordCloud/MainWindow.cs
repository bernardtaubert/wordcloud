using Gtk;
using Gdk;
using GLib;
using System;
using System.IO; // used for TextReader, StringReader, StreamReader, Directory, File and FileInfo
using System.Collections.Generic;
using System.Threading;

namespace WordCloud
{
	public partial class MainWindow: Gtk.Window
	{	
		#region InternalVariables
			private static int MAXSIZE = 42000;
			private static int MINSIZE = 0;
			private static double APPROXWIDTH = 9.2;
			private static double APPROXHEIGHT = 20;
			private static int ACCURACY = 1;
			private static int MINWORDLENGTH = 3;		
		
			private uint numOfKeywords = 0;
			private uint numOfRemovedKeywords = 0;
			private bool thread1InProgress = false; // indicates that thread 1, i.e. a drag and drop is in progress
			private bool thread2InProgress = false; // indicates that thread 2, i.e. a word cloud generation is in progress
			private bool thread1finished = false;
			private bool thread2finished = false;
			private bool consoleVisible = true; // indicates that the console view is displayed
			private bool progressKnown = false;
			private bool progressBarCreated = false;
			private int  fileInProgress;
			private int  fileDisplayed;	
			private int  wordInProgress;
			private int  wordDisplayed;
			private ulong overallLength = 0;
			private ulong currentLength = 0;		
			private string dragText;
			private System.Threading.Thread thread1;			
			private System.Threading.Thread thread2;	
			private List<FileInfo> fileList;
			private List<string> whiteList = new List<string> ();
			private List<string> tempWhiteList = new List<string> ();
			private List<string> blackList = new List<string> ();
			private List<string> tempBlackList = new List<string> ();		
			private List<cLabel> labels = new List<cLabel> ();
			private SortedDictionary<string, uint> wordDictionary = new SortedDictionary<string, uint> (); 
			private KeyValuePair<string, uint>[] wordArray = null;
			private System.Timers.Timer progressBarTimerEvent1 = new System.Timers.Timer ();
			private System.Timers.Timer progressBarTimerEvent2 = new System.Timers.Timer ();		
			private enum TargetType {
				String,
				RootWindow
			};
			private enum c {
				upperLeft = 0,
				upperRight,				
				lowerRight,
				lowerLeft,
				Length
			};		
			private static TargetEntry [] target_table = new TargetEntry [] {
				new TargetEntry ("STRING", 0, (uint) TargetType.String ),
				new TargetEntry ("text/plain", 0, (uint) TargetType.String),
				new TargetEntry ("application/x-rootwindow-drop", 0, (uint) TargetType.RootWindow)
			};
		#endregion
		
		#region GUIElements
			Table outerTable = new Table (1, 1, false);
			Table innerTable = new Table (1, 1, false);
			Table tableOfKeywords = new Table (1, 1, false);
			Table tableOfProgress;
			ScrolledWindow outerScrolledWindow;
			ScrolledWindow innerScrolledWindow;		
			cTextView textView;
			Entry entry;
			Fixed f;
			ProgressBar progressBar;
		#endregion
		
		#region EventHandlers			
			private void HandleDragDataReceived (object sender, DragDataReceivedArgs args)
			{	
				if (!thread1InProgress && !thread2InProgress) {
					thread1finished = false;
					thread1InProgress = true;
					progressKnown = false;
					textView.Show ();
					textView.Buffer.Clear ();
					textView.Buffer.Text = "Calculating ...";
					if (thread1 != null)
						thread1.Abort ();				
					dragText = args.SelectionData.Text;
					ChangeCursor (CursorType.Watch);	
					SetupProgressBar ();
					WaitThread1 (true);	
					
					ThreadStart threadStart = new ThreadStart (HandleDragData);
					System.Threading.Thread t = new System.Threading.Thread (threadStart);
					thread1 = t;				
					t.Start ();		
				} else {
				}
				Gtk.Drag.Finish (args.Context, true, false, args.Time);
			}
		
			private void HandleDragData () {
				tempWhiteList = whiteList;
				tempBlackList = blackList;
				wordDictionary.Clear ();
				if (dragText.StartsWith ("file:///")) { // Parse File Information
					string line;
					string infilePath;
					StringReader inputFiles = new StringReader ( dragText );
					fileList = new List<FileInfo> ();
					while ((line = inputFiles.ReadLine ()) != null) {
						infilePath = line.Remove (0, 7).Replace("\r\n", "").Replace("%20", " "); // Reformat the file path
						if (File.Exists (infilePath)) {                   // Check if the dropped element specifies a filename
							fileList.Add ( new FileInfo ( infilePath ) );
						} else if (Directory.Exists (infilePath)) {       // Check if the dropped element specifies a directory
							foreach (string s in Directory.GetFiles (infilePath, "*", SearchOption.AllDirectories)) {
								fileList.Add ( new FileInfo (s) );
							}
						} else { // if it is not a file, nor a directory, parse as String data
							StringReader inputStream = new StringReader (dragText);
							ParseInputStream (inputStream);
							inputStream.Close ();
						}
					}
					overallLength = 0;
					currentLength = 0;
					foreach (FileInfo f in fileList) {
						overallLength += (ulong)f.Length;
					}
					fileInProgress = 0;
					fileDisplayed = 0;
					foreach (FileInfo f in fileList) {
						fileInProgress++;
						if (!progressKnown && fileList.Count > 1)
							progressKnown = true;
						if (f.Length == 0)
							continue;
						StreamReader fileStream = new StreamReader ( f.OpenRead () );
						ParseInputStream (fileStream);
						currentLength += (ulong)f.Length;
						fileStream.Close ();
					}
				} else { // parse as String data
					StringReader inputStream = new StringReader (dragText);
					ParseInputStream (inputStream);
					inputStream.Close ();
				}
				wordArray = new KeyValuePair<string, uint>[wordDictionary.Count];		
				wordDictionary.CopyTo (wordArray, 0);
				for (int i = 0; i < wordArray.Length; i++) { // create a copy and sort it by Value in descending order
					for (int j = i; j < wordArray.Length; j++) {
						if ( wordArray[i].Value < wordArray[j].Value ) {
							KeyValuePair<string, uint> tempKvp = wordArray [i];
							wordArray [i] = wordArray [j];
							wordArray [j] = tempKvp;
						}
					}
				}
				dragText = "";
				foreach (KeyValuePair<string, uint> kvp in wordArray) {
					dragText += kvp.Key + " " + kvp.Value + "\n";
				}
				thread1InProgress = false;
			}
		
			[GLib.ConnectBefore ()] // The [GLib.ConnectBefore ()] attribute is used to make sure that this custom event handler is invoked before the default event handler
			private void HandleTextViewButtonPressReceived (object sender, ButtonPressEventArgs args) {		
				if (args.Event.Button == 2 && !thread1InProgress) { // on Mouse Button 2 pressed (=middle Mouse Button) and while NO drop in progress, show word cloud view
					if (consoleVisible && thread1finished && !thread2InProgress && wordDictionary.Count != 0) {
						consoleVisible = false;
						textView.Hide ();					
						if (!thread2finished) {
							wordDisplayed = 0;
							wordInProgress = 0;
							thread2InProgress = true;	
							ChangeCursor (CursorType.Watch);					
							if (thread2 != null)
								thread2.Abort ();	
							foreach (cLabel cLabel in labels) {
								cLabel.Destroy ();
							}
							labels.Clear ();
							cLabel l = new cLabel (wordArray[0].Key, MAXSIZE, 0, 0);
							for (int j = 0; j < 4; j++)
								l.corners[(int)c.upperLeft].isFree[j] = false;
							l.corners[(int)c.upperRight].isFree[(int)c.upperLeft] = false;
							l.corners[(int)c.upperRight].isFree[(int)c.upperRight] = false;
							l.corners[(int)c.upperRight].isFree[(int)c.lowerLeft] = false;						
							l.corners[(int)c.lowerLeft].isFree[(int)c.upperLeft] = false;
							l.corners[(int)c.lowerLeft].isFree[(int)c.upperRight] = false;
							l.corners[(int)c.lowerLeft].isFree[(int)c.lowerLeft] = false;							
							labels.Add (l);					
							SetupProgressBar ();
							WaitThread2 (true);
	
							ThreadStart threadStart = new ThreadStart (HandleTextViewButtonPress);
							System.Threading.Thread t = new System.Threading.Thread (threadStart);
							thread2 = t;
							t.Start ();
						}
					}
					else {
						consoleVisible = true;
						textView.Show ();
					}
				}
			}
		
			private void HandleTextViewButtonPress () {
				int maxValue = (int)wordArray[0].Value;
				int maxSize = MAXSIZE;
				int curSize = maxSize;
				for (int i = 1; i < wordArray.Length; i++) { // then iterate over the remaining words and place new labels sequentially		
					cLabel l;
					int x = 0, y = 0;
					int closestL, closestC, closestP = 0; // closest label, corner and distance
					double closestD = -1;                 // closest distance
					double curD;
					KeyValuePair<string, uint> kvp = wordArray[i];
					curSize = ((int)kvp.Value * maxSize) / maxValue;
					if (curSize < MINSIZE) 
						break; // abort if the size is too small
					l = new cLabel (kvp.Key, curSize); // current label which shall be placed closest to the first label
					for (int curL = 0; curL < labels.Count; curL++) {
						for (int curC = 0; curC < labels[curL].corners.Length; curC++) {
							for (int curP = 0; curP < (int)c.Length; curP++) {
								if (labels[curL].corners[curC].isFree[curP]) {
									switch (curP) {
										case ((int)c.upperLeft):
											x = labels[curL].corners[curC].x - l.width;
											y = labels[curL].corners[curC].y - l.height;
											break;
										case ((int)c.upperRight):
											x = labels[curL].corners[curC].x;
											y = labels[curL].corners[curC].y - l.height;											
											break;
										case ((int)c.lowerRight):
											x = labels[curL].corners[curC].x;
											y = labels[curL].corners[curC].y;												
											break;											
										case ((int)c.lowerLeft):
											x = labels[curL].corners[curC].x - l.width;
											y = labels[curL].corners[curC].y;												
											break;															
										default:
											break;
									}
									if (x < 0 || y < 0)
										continue; // continue at negative placement
									if (ValidatePlacement (x, y, l.width, l.height)) {
										curD = MeasureDistance (x, y, l.width, l.height);
										if (closestD < 0) { // this situation only occurs during initialization
											closestD = curD;
											closestC = curC;
											closestL = curL;
											closestP = curP;
										}
										else {
											if (curD < closestD) { // normal comparison to store new closest placement
												closestD = curD;
												closestC = curC;
												closestL = curL;
												closestP = curP;
											}
										}
									}
								}
							}
						}
					}
					// searching for the closest distance has completed. Now set the final position and put the label
					switch (closestP) {
						case ((int)c.upperLeft):
							x = labels[closestL].corners[closestC].x - l.width;
							y = labels[closestL].corners[closestC].y - l.height;
							break;
						case ((int)c.upperRight):
							x = labels[closestL].corners[closestC].x;
							y = labels[closestL].corners[closestC].y - l.height;											
							break;
						case ((int)c.lowerRight):
							x = labels[closestL].corners[closestC].x;
							y = labels[closestL].corners[closestC].y;												
							break;											
						case ((int)c.lowerLeft):
							x = labels[closestL].corners[closestC].x - l.width;
							y = labels[closestL].corners[closestC].y;												
							break;															
						default:
							break;
					}	
					labels[closestL].corners[closestC].isFree[closestP] = false;
					l.SetPosition (x, y);
					labels.Add (l);
					wordInProgress++;
				}
				thread2InProgress = false;
			}
		
			private bool ValidatePlacement (int x, int y, int width, int height) { // this function validates if any of the 4 corner points of a labels rectangle is located inside of an existing labels rectangular area
				int[] xPos = new int[(int)c.Length]; // stores the x coordinates of the 4 corner points of the labels rectangle
				int[] yPos = new int[(int)c.Length]; // stores the y coordinates of the 4 corner points of the labels rectangle
				for (int curP = 0; curP < (int)c.Length; curP++) {
					switch (curP) {
						case ((int)c.upperLeft):
							xPos[(int)c.upperLeft] = x;
							yPos[(int)c.upperLeft] = y;
							break;
						case ((int)c.upperRight):
							xPos[(int)c.upperRight] = x + width;
							yPos[(int)c.upperRight] = y;
							break;
						case ((int)c.lowerRight):
							xPos[(int)c.lowerRight] = x + width;
							yPos[(int)c.lowerRight] = y + height;
							break;											
						case ((int)c.lowerLeft):
							xPos[(int)c.lowerLeft] = x;
							yPos[(int)c.lowerLeft] = y + height;
							break;															
						default:
							break;
					}
				}
				int w, h;
				if (height > width) {
					h = width;
					w = height;
				} else {
					w = width;
					h = height;
				}
				int ptY;
				foreach (cLabel l in labels) { // iterate over the existing labels
					// the following statements check if the x, y coordinates of the label are located inside of another labels area											
					switch (ACCURACY) {
						case 1:
							for (ptY = y; ptY < y + height; ptY++) {
								for (int ptX = x; ptX < x + width; ptX++) {
									if (ptX > l.x && ptY > l.y &&
							    		ptX < l.corners[(int)c.lowerRight].x && ptY < l.corners[(int)c.lowerRight].y) 
										return false;
								}
							}
							break;
						case 0:
						default:					
							for (int ptX = x; ptX < xPos[(int)c.upperRight]; ptX++) {
								ptY = (int) (y + h * (ptX - (double)x / w));
								if (ptX > l.x && ptY > l.y &&
						    		ptX < l.corners[(int)c.lowerRight].x && ptY < l.corners[(int)c.lowerRight].y) 
									return false;
							}	
							for (int curP = 0; curP < (int)c.Length; curP++) {
								if ((xPos[curP] + 1) > l.x && (yPos[curP] + 1) > l.y &&
							    	(xPos[curP]) < l.corners[(int)c.lowerRight].x && (yPos[curP]) < l.corners[(int)c.lowerRight].y)
									return false;						
							}
							break;
					}
				}
				return true;
			}
		
			private double MeasureDistance (int x, int y, int width, int height) { // this function measures the distance from the center of a labels rectangle as specified by the coordinates x, y and its width and height to the coordinate x2=0, y2=0
				double centerX = x + (double)width/2;
				double centerY = y + (double)height/2;
				return Math.Sqrt (Math.Pow (centerX, 2) + Math.Pow (centerY, 2));
			}
		
			[GLib.ConnectBefore ()]
			private void HandleKeyPressEventTextView (object sender, KeyPressEventArgs args) { 
				if ((args.Event.KeyValue == (uint)Gdk.Key.Return || args.Event.KeyValue == (uint)Gdk.Key.KP_Enter) &&
			    	args.Event.State.HasFlag(Gdk.ModifierType.ShiftMask)) {
					AddWordToBlackList ();
					EntryClear ();
					return;
				} else if (args.Event.KeyValue == (uint)Gdk.Key.Return || 
					args.Event.KeyValue == (uint)Gdk.Key.KP_Enter) {
					AddWordToWhiteList ();			
					EntryClear ();
					return;
				}
			}
		
			[GLib.ConnectBefore ()]
			private void HandleButtonPressDestroyWhiteListParent (object sender, ButtonPressEventArgs args) { 
				((Widget)sender).Parent.Destroy ();
				whiteList.Remove (((Widget)sender).Name);
				numOfKeywords--;
				numOfRemovedKeywords++;
				if (numOfKeywords == 0) {
					tableOfKeywords.HeightRequest = 0;
					numOfRemovedKeywords = 0;
				}			
			}
		
			[GLib.ConnectBefore ()]
			private void HandleButtonPressDestroyBlackListParent (object sender, ButtonPressEventArgs args) { 
				((Widget)sender).Parent.Destroy ();
				blackList.Remove (((Widget)sender).Name);
				numOfKeywords--;
				numOfRemovedKeywords++;
				if (numOfKeywords == 0) {
					tableOfKeywords.HeightRequest = 0;
					numOfRemovedKeywords = 0;
				}			
			}		
	
			private void PollThread1 (object sender, System.Timers.ElapsedEventArgs args) {
				Gdk.Threads.Enter ();
				if (progressKnown) {
					if (overallLength != 0)
						SetProgress ( (double) currentLength / overallLength );
					for (int i = fileDisplayed; i < fileInProgress; i++) {
						textView.Buffer.Insert (textView.Buffer.EndIter, "\nParsing " + fileList[i].Name);	
						fileDisplayed++;
					}
				}
				else {
					progressBar.Pulse ();
				}
				if (!thread1InProgress) {
					HideProgressBar ();
					textView.Buffer.Clear ();				
					textView.Buffer.Text = dragText;
					ChangeCursor (CursorType.Arrow);
					thread2finished = false;
					thread1finished = true;
					WaitThread1 (false);
				}
				Gdk.Threads.Leave ();
			}	
		
			private void PollThread2 (object sender, System.Timers.ElapsedEventArgs args) {		
				Gdk.Threads.Enter ();
				for (int i = wordDisplayed; i <= wordInProgress; i++) {
					f.Put (labels[i], labels[i].x, labels[i].y);
					wordDisplayed++;
				}
				if (!thread2InProgress) {
					HideProgressBar ();
					ChangeCursor (CursorType.Arrow);
					thread2finished = true;
					WaitThread2 (false);
				}
				progressBar.Pulse ();
				Gdk.Threads.Leave ();
			}
		#endregion

		public MainWindow (string title): base (Gtk.WindowType.Toplevel)
		{	
			outerScrolledWindow = new ScrolledWindow ();
			outerScrolledWindow.VscrollbarPolicy = PolicyType.Never;
			outerScrolledWindow.AddWithViewport (outerTable);
			
				outerTable.Attach (tableOfKeywords, 0, 1, 0, 1, AttachOptions.Expand | AttachOptions.Fill, AttachOptions.Shrink, 0, 0);
			
				entry = new Entry (); // Create an Entry
				entry.Text = "enter keyword";
				outerTable.Attach (entry, 0, 1, 1, 2, AttachOptions.Expand | AttachOptions.Fill, AttachOptions.Shrink, 0, 0);			
			
					textView = new cTextView (); // Create a TextView
					textView.Buffer.Text = "Drop File";			
					textView.CanFocus = false;
					textView.Editable = false;
			
					Color color = new Color (48, 10, 36);
					textView.ModifyBase (StateType.Normal, color);
					color = new Color (255, 255, 255);			
					textView.ModifyText (StateType.Normal, color);
			
					Pango.FontDescription font = new Pango.FontDescription ();	
					font.Size = 10810;
					font.Family = "Monospace";
					textView.ModifyFont ( font );
	
				EventBox eventBox = new EventBox ();
				f = new Fixed ();						
				color = new Color (48, 10, 36);
				eventBox.ModifyBg (StateType.Normal, color);	
				eventBox.Show ();
				eventBox.Add (f);
				f.Show ();			
				innerScrolledWindow = new ScrolledWindow (); // Create a ScrolledWindow as Container for the Table
				innerScrolledWindow.HscrollbarPolicy = PolicyType.Never;
				innerTable.Attach (textView, 0, 1, 0, 1, AttachOptions.Expand | AttachOptions.Fill, AttachOptions.Expand | AttachOptions.Fill, 0, 0);
				innerTable.Attach (eventBox, 0, 1, 0, 1, AttachOptions.Expand | AttachOptions.Fill, AttachOptions.Expand | AttachOptions.Fill, 0, 0);					
				innerScrolledWindow.AddWithViewport (innerTable); // Add the table as Container for the widgets
				outerTable.Attach (innerScrolledWindow, 0, 1, 2, 3, AttachOptions.Expand | AttachOptions.Fill, AttachOptions.Expand | AttachOptions.Fill, 0, 0);
			
				ReadFiles ();
			
			f.ButtonPressEvent += new ButtonPressEventHandler (HandleTextViewButtonPressReceived);
			eventBox.ButtonPressEvent += new ButtonPressEventHandler (HandleTextViewButtonPressReceived);	
			textView.ButtonPressEvent += new ButtonPressEventHandler (HandleTextViewButtonPressReceived);
			entry.KeyPressEvent += new KeyPressEventHandler (HandleKeyPressEventTextView);
			progressBarTimerEvent1.Interval = 50;
			progressBarTimerEvent1.Elapsed += new System.Timers.ElapsedEventHandler(PollThread1);		
			progressBarTimerEvent2.Interval = 50;
			progressBarTimerEvent2.Elapsed += new System.Timers.ElapsedEventHandler(PollThread2);				
			Gtk.Drag.DestSet (this, DestDefaults.All, target_table, DragAction.Copy | DragAction.Move | DragAction.Default);
			Gtk.Drag.DestSet (textView, DestDefaults.All, target_table, DragAction.Copy | DragAction.Move | DragAction.Default);
			textView.DragDataReceived += new DragDataReceivedHandler (HandleDragDataReceived);
			this.DragDataReceived += new DragDataReceivedHandler (HandleDragDataReceived);
			this.Add (outerScrolledWindow);
			this.AllowShrink = true;
			this.Title = title;	
			Build ();
		}
		
		private void ReadFiles () {
			int start, end;
			string line;
			FileInfo f;
			if (File.Exists ("settings")) {
				f = new FileInfo ("settings");
				StreamReader fileStream = new StreamReader ( f.OpenRead () );
				while ((line = fileStream.ReadLine ()) != null) {
					if (line.StartsWith("MAXSIZE")) {
						start = line.IndexOf ("=");
						end   = line.IndexOf ("#");
						try {
							if (start >= 0 && end > 0)
								MAXSIZE = int.Parse (line.Substring (start + 1, end - start - 1));
							else if (start >= 0)
								MAXSIZE = int.Parse (line.Substring (start + 1, line.Length - start - 1));
						} catch (Exception e) {
							Console.WriteLine ("Error while parsing the settings file.\n" +e.StackTrace);
						}
					} else if (line.StartsWith("MINSIZE")) {
						start = line.IndexOf ("=");
						end   = line.IndexOf ("#");
						try {
							if (start >= 0 && end > 0)
								MINSIZE = int.Parse (line.Substring (start + 1, end - start - 1));
							else if (start >= 0)
								MINSIZE = int.Parse (line.Substring (start + 1, line.Length - start - 1));
						} catch (Exception e) {
							Console.WriteLine ("Error while parsing the settings file.\n" +e.StackTrace);
						}
					} else if (line.StartsWith("APPROXWIDTH")) {
						start = line.IndexOf ("=");
						end   = line.IndexOf ("#");
						try {
							if (start >= 0 && end > 0)
								APPROXWIDTH = double.Parse (line.Substring (start + 1, end - start - 1));
							else if (start >= 0)
								APPROXWIDTH = double.Parse (line.Substring (start + 1, line.Length - start - 1));
						} catch (Exception e) {
							Console.WriteLine ("Error while parsing the settings file.\n" +e.StackTrace);
						}
					} else if (line.StartsWith("APPROXHEIGHT")) {
						start = line.IndexOf ("=");
						end   = line.IndexOf ("#");
						try {
							if (start >= 0 && end > 0)
								APPROXHEIGHT = double.Parse (line.Substring (start + 1, end - start - 1));
							else if (start >= 0)
								APPROXHEIGHT = double.Parse (line.Substring (start + 1, line.Length - start - 1));
						} catch (Exception e) {
							Console.WriteLine ("Error while parsing the settings file.\n" +e.StackTrace);
						}
					} else if (line.StartsWith("ACCURACY")) {
						start = line.IndexOf ("=");
						end   = line.IndexOf ("#");
						try {
							if (start >= 0 && end > 0)
								ACCURACY = int.Parse (line.Substring (start + 1, end - start - 1));
							else if (start >= 0)
								ACCURACY = int.Parse (line.Substring (start + 1, line.Length - start - 1));
						} catch (Exception e) {
							Console.WriteLine ("Error while parsing the settings file.\n" +e.StackTrace);
						}
					} else if (line.StartsWith("MINWORDLENGTH")) {
						start = line.IndexOf ("=");
						end   = line.IndexOf ("#");
						try {
							if (start >= 0 && end > 0)
								MINWORDLENGTH = int.Parse (line.Substring (start + 1, end - start - 1));
							else if (start >= 0)
								MINWORDLENGTH = int.Parse (line.Substring (start + 1, line.Length - start - 1));
						} catch (Exception e) {
							Console.WriteLine ("Error while parsing the settings file.\n" +e.StackTrace);
						}
					}				
				}
				fileStream.Close ();
			}
			if (File.Exists ("blacklist")) {
				int i;
				string tempWord = "";
				f = new FileInfo ("blacklist");
				StreamReader fileStream = new StreamReader ( f.OpenRead () );				
				while (( i = fileStream.Read ()) != -1) {
					string tempChar = "";
					try {
						tempChar = char.ConvertFromUtf32 (i).ToLower ();
					} catch (ArgumentOutOfRangeException e) {
						Console.WriteLine ("Error: \n" + e.StackTrace);
						tempWord = "";
						continue; // Conversion failed on unknown character, continue with next char
					}
					if ( IsAlphanumeric (tempChar[0])) {
						tempWord += tempChar;
					}
					else {
						if (tempWord.Length >= MINWORDLENGTH) {
							if (!IsNumeric (tempWord) && !blackList.Contains (tempWord)) { // if it is not a numeric value and not inside the blacklist
								blackList.Add (tempWord);                                  // then add the word
							}
					    }
						tempWord = "";
					}
				}
				fileStream.Close ();
			}
			if (File.Exists ("whitelist")) {
				int i;
				string tempWord = "";
				f = new FileInfo ("blacklist");
				StreamReader fileStream = new StreamReader ( f.OpenRead () );				
				while (( i = fileStream.Read ()) != -1) {
					string tempChar = "";
					try {
						tempChar = char.ConvertFromUtf32 (i).ToLower ();
					} catch (ArgumentOutOfRangeException e) {
						Console.WriteLine ("Error: \n" + e.StackTrace);
						tempWord = "";
						continue; // Conversion failed on unknown character, continue with next char
					}
					if ( IsAlphanumeric (tempChar[0])) {
						tempWord += tempChar;
					}
					else {
						if (tempWord.Length >= MINWORDLENGTH) {
							if (!IsNumeric (tempWord) && !blackList.Contains (tempWord)) { // if it is not a numeric value and not blacklisted and
								if (!whiteList.Contains (tempWord)) {                      // if not already inside the whitelist
									wordDictionary.Add (tempWord, 1);                      // then add the word
								}
							}
					    }
						tempWord = "";
					}
				}
				fileStream.Close ();				
			}
			if (File.Exists ("input")) {
				int i;
				string text;
				f = new FileInfo ("input");			
				StreamReader fileStream = new StreamReader ( f.OpenRead () );
				ParseInputStream (fileStream);
				fileStream.Close ();
				wordArray = new KeyValuePair<string, uint>[wordDictionary.Count];		
				wordDictionary.CopyTo (wordArray, 0);
				for (i = 0; i < wordArray.Length; i++) { // create a copy and sort it by Value in descending order
					for (int j = i; j < wordArray.Length; j++) {
						if ( wordArray[i].Value < wordArray[j].Value ) {
							KeyValuePair<string, uint> tempKvp = wordArray [i];
							wordArray [i] = wordArray [j];
							wordArray [j] = tempKvp;
						}
					}
				}
				text = "";
				foreach (KeyValuePair<string, uint> kvp in wordArray) {
					text += kvp.Key + " " + kvp.Value + "\n";
				}
				textView.Buffer.Clear ();				
				textView.Buffer.Text = text;					
				thread2finished = false;
				thread1finished = true;
			}			
		}
		
		public void AddWordToWhiteList () {
			string[] words = entry.Text.Split(' ');
			foreach (string text in words) {
				string tempText = text.ToLower ();
				if (tempText.Length >= MINWORDLENGTH) {
					foreach (string s in whiteList) {
						if (s.Equals (tempText)) {
							return; // exit, if the KeyWord is already inside the List
						}
					}
					foreach (string s in blackList) {
						if (s.Equals (tempText)) {
							return; // exit, if the KeyWord is already inside the List
						}
					}					
					if (numOfKeywords == 0) { // Initialization or when no keywords exist
						tableOfKeywords.HeightRequest = 20;
						tableOfKeywords.Show ();
					}							
					Gtk.Image img = Gtk.Image.NewFromIconName ("gtk-close", Gtk.IconSize.Button);	
					EventBox eventBoxImage = new EventBox();
					eventBoxImage.Name = tempText;
					eventBoxImage.Add (img);	
					eventBoxImage.Show ();					
					img.Show ();
					
					Label label = new Label (tempText);
					EventBox eventBoxLabel = new EventBox();
					eventBoxLabel.Name = tempText;
					eventBoxLabel.Add (label);		
					eventBoxLabel.Show ();			
					label.Show ();				

					HBox hb = new HBox();					
					hb.PackStart (eventBoxLabel, false, false, 0);
					hb.PackStart (eventBoxImage, false, false, 0);
					hb.Show ();
					
					uint currentRow = numOfRemovedKeywords + numOfKeywords;
					tableOfKeywords.Attach (hb, currentRow, currentRow+1, 0, 1, AttachOptions.Shrink, AttachOptions.Shrink, 0, 0); // not required: tableOfKeywords.Show (););
					eventBoxImage.ButtonPressEvent += new ButtonPressEventHandler (HandleButtonPressDestroyWhiteListParent);
					eventBoxLabel.ButtonPressEvent += new ButtonPressEventHandler (HandleButtonPressDestroyWhiteListParent);
													
					whiteList.Add (tempText);
					numOfKeywords++;	
					EntryClear ();
				} 
			}
		}
		
		public void AddWordToBlackList () {
			string[] words = entry.Text.Split(' ');
			foreach (string text in words) {
				string tempText = text.ToLower ();
				if (tempText.Length >= MINWORDLENGTH) {
					foreach (string s in whiteList) {
						if (s.Equals (tempText)) {
							return; // exit, if the KeyWord is already inside the List
						}
					}					
					foreach (string s in blackList) {
						if (s.Equals (tempText)) {
							return; // exit, if the KeyWord is already inside the List
						}
					}
					if (numOfKeywords == 0) { // Initialization or when no keywords exist
						tableOfKeywords.HeightRequest = 20;
						tableOfKeywords.Show ();
					}							
					Gtk.Image img = Gtk.Image.NewFromIconName ("gtk-close", Gtk.IconSize.Button);	
					EventBox eventBoxImage = new EventBox();
					eventBoxImage.Name = tempText;
					eventBoxImage.Add (img);				
					eventBoxImage.Show ();					
					img.Show ();
					
					Label label = new Label ();
					EventBox eventBoxLabel = new EventBox();
					eventBoxLabel.Name = tempText;
					eventBoxLabel.Add (label);	
					eventBoxLabel.Show ();	
					label.Markup = "<span strikethrough=\"true\">" + tempText + "</span>";
					label.Show ();			
					
					HBox hb = new HBox();					
					hb.PackStart (eventBoxLabel, false, false, 0);
					hb.PackStart (eventBoxImage, false, false, 0);
					hb.Show ();
					
					uint currentRow = numOfRemovedKeywords + numOfKeywords;
					tableOfKeywords.Attach (hb, currentRow, currentRow+1, 0, 1, AttachOptions.Shrink, AttachOptions.Shrink, 0, 0); // not required: tableOfKeywords.Show (););
					eventBoxImage.ButtonPressEvent += new ButtonPressEventHandler (HandleButtonPressDestroyBlackListParent);
					eventBoxLabel.ButtonPressEvent += new ButtonPressEventHandler (HandleButtonPressDestroyBlackListParent);
													
					blackList.Add (tempText);
					numOfKeywords++;	
					EntryClear ();
				} 
			}
		}
		
		public void EntryClear () {
			entry.Text = "";
		}
		
		protected void OnDeleteEvent (object sender, DeleteEventArgs a)
		{ 
			Application.Quit ();
			a.RetVal = true;
		}
		
		private bool IsAlphanumeric (char c) {
			if ((c >= 48) && (c <= 57))
				return true; // digits 0-9
			else if ((c >= 65) && (c <= 90))
				return true; // large letters A-Z
			else if ((c >= 97) && (c <= 122))
				return true; // small letters a-z
			else
				switch ((int)c) {
					case 196:				
					case 214:
					case 220:
					case 223:				
					case 228:
					case 246:				
					case 252:
						return true; // German language special characters
					default:
						return false; // other characters are currently not supported
				}
		}
		
		private bool IsNumeric (string s) {
			foreach (char c in s) {
				if ((c >= 48) && (c <= 57)) { // digits 0-9
					// do nothing
				}
				else return false;
			}
			return true;
		}
					
		private void ChangeCursor (Gdk.CursorType type) {
			this.GdkWindow.Cursor = new Gdk.Cursor(type);
		}
			
		private void WaitThread1 (bool b) {
			if (b) {		
				progressBarTimerEvent1.Start ();		
			}
			else {
				progressBarTimerEvent1.Stop ();		
			}
		}
		
		private void WaitThread2 (bool b) {
			if (b) {		
				progressBarTimerEvent2.Start ();		
			}
			else {
				progressBarTimerEvent2.Stop ();		
			}
		}		
		
		private void SetProgress (double progress) {
			progressBar.Fraction = progress;
		}
		
		private void SetupProgressBar () {
			if (!progressBarCreated) {
				progressBar = new ProgressBar (); // Create a ProgressBar
				progressBar.HeightRequest = 12;
				progressBar.Show ();
				tableOfProgress = new Table (1, 1, false);
				tableOfProgress.Attach (progressBar, 0, 1, 0, 1, AttachOptions.Expand | AttachOptions.Fill, AttachOptions.Shrink, 0, 0);
				tableOfProgress.HeightRequest = 12;
				tableOfProgress.Show ();			
				outerTable.Attach (tableOfProgress, 0, 1, 3, 4, AttachOptions.Expand | AttachOptions.Fill, AttachOptions.Shrink, 0, 0);
				progressBarCreated = true;
			} else {
				tableOfProgress.Show ();			
				progressBar.Show ();
			}
		}
		
		private void HideProgressBar () {
			tableOfProgress.Hide ();			
			progressBar.Hide ();
		}
		
		private void ParseInputStream (TextReader inputStream) { // This function parses an input stream by converting all characters to lowercase and splitting all words on any non alphanumeric delimiters
			int i;
			string tempWord = "";
			while (( i = inputStream.Read ()) != -1) {
				string tempChar = "";
				try {
					tempChar = char.ConvertFromUtf32 (i).ToLower ();
				} catch (ArgumentOutOfRangeException e) {
					Console.WriteLine ("Error: \n" + e.StackTrace);
					tempWord = "";
					continue; // Conversion failed on unknown character, continue with next char
				}
				if ( IsAlphanumeric (tempChar[0])) {
					tempWord += tempChar;
				}
				else {
					if (tempWord.Length >= MINWORDLENGTH) {
						if (!IsNumeric (tempWord) && !tempBlackList.Contains (tempWord)) {       // if it is not a numeric value and not blacklisted and
							if (tempWhiteList.Count == 0 || tempWhiteList.Contains (tempWord)) { // if no words are whitelisted at all or it at least this word is whitelisted
								if (!wordDictionary.ContainsKey (tempWord))                      // and if the word is not already inside the dictionary
									wordDictionary.Add (tempWord, 1);                            // then add the word 
								else {
									wordDictionary[tempWord]++; // if already inside, then just increment its count
								}
							}
						}
				    }
					tempWord = "";
				}
			}
		}
		
		private class cLabel : Gtk.Label
		{

			public int x;
			public int y;
			public int width; 
			public int height;
			public Corner[] corners; // the 4 corners of the rectangle of the cLabel consist of their x,y positions and additional placement information
			
			public cLabel (string s, int size) {
				SetFont (s, size);
			}
			
			public cLabel (string s, int size, int x, int y) {
				SetFont (s, size);
				SetPosition (x, y);
			}	
			
			public void SetFont (string s, int size) {
				Pango.FontDescription font = new Pango.FontDescription ();	
				font.Family = "Monospace";
				font.Size = size;
				Color color = new Color (255, 255, 255);			
				this.ModifyFg (StateType.Normal, color);	
				this.ModifyFont (font);
				this.Text = s;
				this.Show ();
				this.width = (int)(((s.Length)*APPROXWIDTH*size)/10000); // approximate the labels width, based on the character width
				this.height = (int)((APPROXHEIGHT*size)/11000);          // approximate the labels height, based on the character height				
			}
			
			public void SetPosition (int x, int y) {
				this.x = x;
				this.y = y;
				this.corners = new Corner[(int)c.Length];
				for (int i = 0; i < corners.Length; i++) {
					this.corners[i] = new Corner ();
				}
				// store the corner coordinates:
				this.corners[(int)c.upperLeft].x = x;
				this.corners[(int)c.upperLeft].y = y;
				this.corners[(int)c.upperRight].x = x + width;
				this.corners[(int)c.upperRight].y = y;			
				this.corners[(int)c.lowerRight].x = x + width;
				this.corners[(int)c.lowerRight].y = y + height;	
				this.corners[(int)c.lowerLeft].x = x;
				this.corners[(int)c.lowerLeft].y = y + height;	
				// lockout the placement possibilities of labels
				this.corners[(int)c.upperLeft].isFree[(int)c.lowerRight] = false;
				this.corners[(int)c.upperRight].isFree[(int)c.lowerLeft] = false;
				this.corners[(int)c.lowerRight].isFree[(int)c.upperLeft] = false;				
				this.corners[(int)c.lowerLeft].isFree[(int)c.upperRight] = false;				
			}
		}
		
		public class Corner {
			public int x;
			public int y;
			public bool[] isFree = new bool[(int)c.Length]; // placement information
			
			public Corner () {
				for (int i = 0; i < isFree.Length; i++) {
					isFree[i] = true;
				}
			}
		}	
		
		public class cTextView : Gtk.TextView
		{
			public cTextView () {
			}
				
			protected override bool OnKeyPressEvent (EventKey e) { // override the default OnKeyPressEvent Handler
				return true;
			}
				
			protected override void OnDragDataReceived (DragContext context, int x, int y, SelectionData selection_data, uint info, uint time_) { // override the default DragDataReceived Handler
				return;
			}
		}
	}	
}
