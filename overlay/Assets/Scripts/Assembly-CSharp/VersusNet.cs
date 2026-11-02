using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// One TCP connection on the local network: the host listens, the guest connects to it. Plain .NET sockets
// (they work under IL2CPP on Android and iOS) and no UDP broadcast or multicast, which iOS only allows with
// an extra entitlement. Messages are text lines; reading runs on a thread and the game takes them from
// Receive() on the main thread.
public class VersusNet
{
	public const int DefaultPort = 47123;

	private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();

	private readonly object writeLock = new object();

	private TcpListener listener;

	private TcpClient client;

	private NetworkStream stream;

	private volatile bool connected;

	private volatile bool closed;

	private volatile string error;

	public bool Connected
	{
		get
		{
			return connected && !closed;
		}
	}

	public bool Closed
	{
		get
		{
			return closed;
		}
	}

	// Why the connection could not be made or was lost, in Spanish for the player.
	public string Error
	{
		get
		{
			return error;
		}
	}

	public int Port { get; private set; }

	public void Host(int port)
	{
		Port = port;
		try
		{
			listener = new TcpListener(IPAddress.Any, port);
			listener.Start(1);
		}
		catch (Exception e)
		{
			Fail("No se pudo abrir la partida en el puerto " + port + " (" + e.Message + ")");
			return;
		}
		Thread thread = new Thread(AcceptLoop);
		thread.IsBackground = true;
		thread.Name = "VersusAccept";
		thread.Start();
	}

	public void Join(string address, int port)
	{
		Port = port;
		Thread thread = new Thread(() => ConnectLoop(address, port));
		thread.IsBackground = true;
		thread.Name = "VersusConnect";
		thread.Start();
	}

	private void AcceptLoop()
	{
		try
		{
			TcpClient accepted = listener.AcceptTcpClient();
			// One rival per game: stop listening once someone is in.
			listener.Stop();
			listener = null;
			if (closed)
			{
				accepted.Close();
				return;
			}
			Attach(accepted);
		}
		catch (Exception e)
		{
			if (!closed)
			{
				Fail("Se cerró la espera (" + e.Message + ")");
			}
		}
	}

	private void ConnectLoop(string address, int port)
	{
		try
		{
			TcpClient tcp = new TcpClient(AddressFamily.InterNetwork);
			IAsyncResult result = tcp.BeginConnect(address, port, null, null);
			if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(6)) || !tcp.Connected)
			{
				tcp.Close();
				Fail("No hay ninguna partida en " + address + ". Revisa el código y que los dos estén en la misma wifi.");
				return;
			}
			tcp.EndConnect(result);
			Attach(tcp);
		}
		catch (Exception e)
		{
			Fail("No se pudo conectar con " + address + " (" + e.Message + ")");
		}
	}

	private void Attach(TcpClient tcp)
	{
		tcp.NoDelay = true;
		client = tcp;
		stream = tcp.GetStream();
		connected = true;
		Thread thread = new Thread(ReadLoop);
		thread.IsBackground = true;
		thread.Name = "VersusRead";
		thread.Start();
	}

	private void ReadLoop()
	{
		try
		{
			StreamReader reader = new StreamReader(stream, new UTF8Encoding(false));
			string line;
			while (!closed && (line = reader.ReadLine()) != null)
			{
				inbox.Enqueue(line);
			}
			if (!closed)
			{
				Fail("El rival se desconectó.");
			}
		}
		catch (Exception e)
		{
			if (!closed)
			{
				Fail("Se perdió la conexión con el rival (" + e.Message + ")");
			}
		}
	}

	public bool Receive(out string line)
	{
		return inbox.TryDequeue(out line);
	}

	public void Send(string line)
	{
		if (!Connected)
		{
			return;
		}
		byte[] bytes = Encoding.UTF8.GetBytes(line.Replace('\n', ' ') + "\n");
		try
		{
			lock (writeLock)
			{
				stream.Write(bytes, 0, bytes.Length);
				stream.Flush();
			}
		}
		catch (Exception e)
		{
			Fail("Se perdió la conexión con el rival (" + e.Message + ")");
		}
	}

	public void Fail(string why)
	{
		if (error == null)
		{
			error = why;
		}
		Close();
	}

	public void Close()
	{
		closed = true;
		connected = false;
		try
		{
			if (listener != null)
			{
				listener.Stop();
			}
		}
		catch (Exception)
		{
		}
		try
		{
			if (client != null)
			{
				client.Close();
			}
		}
		catch (Exception)
		{
		}
	}

	// The phone's address on the local network. A UDP "connect" sends nothing: it only asks the system which
	// interface would route there. Falls back to the interface list.
	public static string LocalIPv4()
	{
		try
		{
			using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
			{
				socket.Connect("10.255.255.255", 9);
				IPEndPoint local = socket.LocalEndPoint as IPEndPoint;
				if (local != null && !IPAddress.IsLoopback(local.Address) && !local.Address.Equals(IPAddress.Any))
				{
					return local.Address.ToString();
				}
			}
		}
		catch (Exception)
		{
		}
		try
		{
			foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
				{
					continue;
				}
				foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
				{
					if (info.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(info.Address))
					{
						return info.Address.ToString();
					}
				}
			}
		}
		catch (Exception)
		{
		}
		return "127.0.0.1";
	}

	// "45" becomes the same /24 as this phone (192.168.1.45); a full address is used as it is.
	public static string ResolveCode(string code)
	{
		code = (code ?? string.Empty).Trim();
		if (code.Contains("."))
		{
			return code;
		}
		int last;
		if (!int.TryParse(code, out last) || last < 1 || last > 254)
		{
			return null;
		}
		string mine = LocalIPv4();
		int dot = mine.LastIndexOf('.');
		return (dot < 0) ? null : mine.Substring(0, dot + 1) + last;
	}
}
