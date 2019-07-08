using Fleck;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InteractiveLS
{
	
	/// <summary>
	/// Main class to start and stop a Websocket/TCP server
	/// </summary>
	/// <remarks>
	/// This allows you to send any data you want back and forth between the Streamlabs Chatbot
	/// and your own Web Sockets site / TCP client program, opening a multitude of posibilities.
	/// </remarks>
	public class Interaction
	{
		private static TcpListener server;
		private static TcpClient client;
		private WebSocketServer WSserver;
		private string ServerType;
		private List<IWebSocketConnection> Wsockets;
		private List<DIsposableProcess> programsAlong;

		private Task TCPServerTask;
		private Task WSServerTask;

		private CancellationTokenSource tokenSourceTCP = new CancellationTokenSource();
		private CancellationTokenSource tokenSourceWS = new CancellationTokenSource();
		private CancellationToken ctTCP;
		private CancellationToken ctWS;

		private int TCPPort;
		private int WSPort;

		/// <summary>
		/// Event handler which fires when a message is received from any client
		/// </summary>
		/// <example>
		/// <code>
		/// def IReceivedAMessage(sender,e):
		///		Parent.SendStreamMessage(e.data)
		///	
		/// InteractionInstance += IReceivedAMessage
		/// </code>
		/// </example>
		public event EventHandler MessageReceived;

		public Interaction()
		{
			Console.WriteLine("Started Instance");
			programsAlong = new List<DIsposableProcess>();
			ctTCP = tokenSourceTCP.Token;
			ctWS = tokenSourceWS.Token;
		}

		/// <summary>
		/// Play a beep sound when needed.
		/// </summary>
		/// <remarks>
		/// Mostly used for debugging
		/// </remarks>
		public void Beep()
		{
			SystemSounds.Beep.Play();
		}

		~Interaction()
		{
			
		}

		/// <summary>
		/// If you have your standalone client which connects to TCP server,
		/// you can start it along the script and the script will finish it when unloading.
		/// </summary>
		/// 
		/// <param name="path">string - Path or name of the executable.</param>
		/// <param name="showWindow">bool - Show or hide the window of the executable.</param>
		/// <param name="args">string - single string with all the arguments</param>
		public void StartProgramAlong(string path, bool showWindow, string args = "")
		{
			var info = new ProcessStartInfo();
			info.CreateNoWindow = showWindow;
			info.FileName = path;
			info.WindowStyle = showWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden;
			info.Arguments = args;
			programsAlong.Add(new DIsposableProcess(Process.Start(info)));
		}


		/// <summary>
		/// Finalizes all servers and stop all programs running along with them.
		/// </summary>
		/// <remarks>
		/// This has to be called when Unloading your script.
		/// </remarks>
		public void Close()
		{
			if (Wsockets != null && Wsockets.Count > 0)
			{
				Wsockets.ForEach(s => {
					s.Close();
					s = null;
					Wsockets.Remove(s);
				});
				Wsockets = null;	
			}
			if (server != null)
			{
				server.Stop();
				server = null;
			}
			if (client != null)
			{
				client.Close();
				client = null;
			}
			if (WSserver != null)
			{
				WSserver.Dispose();
				WSserver = null;
			}
			if (programsAlong != null && programsAlong.Count > 0) programsAlong.ForEach(p => {
				p._resource.Kill();
				p.Dispose();
			});



		}


		/// <summary>
		/// Run to start the server you want.
		/// </summary>
		/// <param name="svtype">string - The type of server you want
		/// <para>"ws" for Web Socket Server</para>
		/// <para>"tcp" for TCP Server</para>
		/// </param>
		/// <param name="port">int - The port you want to start the server with</param>
		public void StartServer(string svtype,int port)
		{
			Console.WriteLine("Starting server: "+svtype);
			ServerType = svtype.ToLower();
			switch (svtype.ToLower())
			{
				
				case "tcp":
					TCPPort = port;
					if (server==null)
					{
						Task.Factory.StartNew(StartTCPServer, tokenSourceTCP.Token);
					}
					else
					{
						server.Stop();
						server = null;
						Task.Run(StartTCPServer);

					}
					break;
				case "ws":
					WSPort = port;
					if (WSserver == null)
					{
						Task.Factory.StartNew(StartWebSocketServer, tokenSourceWS.Token);
					}
					else
					{
						WSserver = null;
						Task.Run(StartWebSocketServer);
					}
					break;
				default:
					break;
			}
		}

		public virtual void OnMessageReceived(object sender, EventArgs e)
		{
			EventHandler handler = MessageReceived;
			if (handler != null) handler(this, e);
		}
		

		/// <summary>
		/// Get an instance of the Interaction class to work with
		/// </summary>
		/// <returns>Interaction instance</returns>
		public static Interaction GetInstance()
		{
			return new Interaction();
		}

		private async Task StartWebSocketServer()
		{
			ctWS.ThrowIfCancellationRequested();

			

			WSserver = new WebSocketServer("ws://0.0.0.0:"+WSPort.ToString());
			Wsockets = new List<IWebSocketConnection>();
			WSserver.Start(socket => {
				socket.OnOpen = () => Console.WriteLine("Connection Opened");
				socket.OnClose = () => Wsockets.Remove(socket);
				socket.OnMessage = message => {
					var msgargs = new MessageReceivedEventArgs();
					msgargs.Message = message;
					MessageReceived(this, msgargs);
				};
				socket.OnError = (err) => {
					socket.Close();
				};
				Wsockets.Add(socket);
			});
			
		}

		private async Task StartTCPServer()
		{
			server = new TcpListener(IPAddress.Any, TCPPort);
			// we set our IP address as server's address, and we also set the port: 9999

			server.Start();  // this will start the server

			while (true)   //we wait for a connection
			{
				client = server.AcceptTcpClient();  //if a connection exists, the server will accept it

				NetworkStream ns = client.GetStream(); //networkstream is used to send/receive messages

				//byte[] hello = new byte[100];   //any message must be serialized (converted to byte array)
				//hello = Encoding.Default.GetBytes("hello world");  //conversion string => byte array

				//ns.Write(hello, 0, hello.Length);     //sending the message
				ASCIIEncoding encoder = new ASCIIEncoding();



				while (client.Connected)  //while the client is connected, we look for incoming messages
				{
					byte[] msg = new byte[4096];     //the messages arrive as byte array
					int bytesRead = 0;

					try
					{
						// Read up to 4096 bytes
						bytesRead = ns.Read(msg, 0, 4096);
					}
					catch { /*a socket error has occured*/ }

					
					var messageargs = new MessageReceivedEventArgs();
					messageargs.Message = encoder.GetString(msg, 0, bytesRead);

					//OnMessageReceived(this, EventArgs.Empty);
					MessageReceived(this, messageargs);
				}

			}
		}

		public class MessageReceivedEventArgs : EventArgs
		{
			public string Message { get; set; }
		}

		/// <summary>
		/// Send a message to the clients
		/// </summary>
		/// <param name="msg">string - Message to send to clients</param>
		/// <returns></returns>
		public string SendMessage(string msg)
		{
			try
			{
				if (ServerType == "tcp")
				{
					if (client == null) return "TCP client not connected";
					if (client.GetStream().CanWrite)
					{
						byte[] responseByte = ASCIIEncoding.ASCII.GetBytes(msg);

						client.GetStream().Write(responseByte, 0, responseByte.Length);
						return "Sent to TCP client";
					}
				}
				else if (ServerType == "ws")
				{
					Console.WriteLine("Sending msg to " + Wsockets.Count + " clients!");
					Wsockets.ForEach(socket => socket.Send(msg));
					return "Sent message to" + Wsockets.Count.ToString();
				}
				return "Not found";
			}
			catch (Exception e)
			{

				return "Erro: " + e.Message;
			}
		}

		private class DIsposableProcess : IDisposable
		{
			public Process _resource;
			private bool _disposed=false;

			public DIsposableProcess(Process p)
			{
				_resource = p;
			}

			public void Dispose()
			{
				Dispose(true);
				GC.SuppressFinalize(this);
			}

			public void Dispose(bool disposing)
			{
				if (!_disposed)
				{
					if (disposing)
					{
						Console.WriteLine("Exiting Process. Cleaning up.");
						// free resources here
						if (_resource != null)
						{
							_resource.Kill();
							_resource.Dispose();
						}
							
						Console.WriteLine("Object disposed.");
					}

					// Indicate that the instance has been disposed.
					_resource = null;
					_disposed = true;
				}
			}
		}
	}
}
