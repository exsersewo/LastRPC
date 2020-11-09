using DiscordRPC;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Objects;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LastRPC
{
	public partial class Main : Form
	{
		string configLocation = Path.Combine(AppContext.BaseDirectory, "config.json");
		Config config;
		LastfmClient lastfmClient;
		DiscordRpcClient rpcClient;
		Thread lastFMThread;
		LastTrack currentTrack;
		bool isRunning = false;

		public Main()
		{
			if (File.Exists(configLocation))
			{
				config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configLocation));
			}

			InitializeComponent();

			if (config != null)
			{
				lastFMKey.Text = config.LastFMKey;
				lastFMSecret.Text = config.LastFMSecret;
				discordAppId.Text = config.DiscordId;
				lastFMUsername.Text = config.Username;
			}
			else
			{
				config = new Config();
			}

			stop.Enabled = false;

			statusLabel.Text = "Ready";
		}

		private void notifier_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			Show();
			WindowState = FormWindowState.Normal;
		}

		private void Main_Resize(object sender, System.EventArgs e)
		{
			if (WindowState == FormWindowState.Minimized)
			{
				Hide();
			}
		}

		private void Configure()
		{
			lastfmClient = new LastfmClient(lastFMKey.Text, lastFMSecret.Text);

			rpcClient = new DiscordRpcClient(discordAppId.Text ?? "775484756380352542");
			config.Set(lastFMKey.Text, lastFMSecret.Text, discordAppId.Text, lastFMUsername.Text);

			File.WriteAllText(configLocation, JsonConvert.SerializeObject(config));
		}

		private string GetLogMessage(string status, string key, string message)
			=> $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] [{status}] [{key}] {message}";

		private void WriteLog(string message)
		{
			if (InvokeRequired)
			{
				this.Invoke(new Action<string>(WriteLog), new object[] { message });
				return;
			}

			logTextBox.AppendText(message);
			logTextBox.AppendText(Environment.NewLine);
		}

		private void StartLastFMScrobbler()
		{
			string username = lastFMUsername.Text;
			statusLabel.Text = "Running";
			isRunning = true;
			lastFMThread = new Thread(async () =>
			{
				Thread.CurrentThread.IsBackground = true;

				while (isRunning)
				{
					WriteLog(GetLogMessage("I", "LastFM Request", "Getting User Scrobbles"));

					var scrobbles = await lastfmClient.User.GetRecentScrobbles(username, extendedResponse: true, count: 1);

					if (scrobbles.Success)
					{
						if (scrobbles.Content.Count > 0)
						{
							var mostRecent = scrobbles.Content[0];

							if (mostRecent.IsNowPlaying.HasValue && mostRecent.IsNowPlaying.Value)
							{
								WriteLog(GetLogMessage("I", "LastFM Request", "Found Currently playing"));

								if (currentTrack == null || mostRecent.Mbid != currentTrack.Mbid)
								{
									WriteLog(GetLogMessage("I", "LastFM Request", "Different song, setting Status"));
									SetDiscordStatus(mostRecent, true);
								}
							}

							currentTrack = mostRecent;
						}
						else
						{
							WriteLog(GetLogMessage("E", "LastFM Request", "Got Nothing Waiting 30s"));
						}
					}
					else
					{
						WriteLog(GetLogMessage("E", "LastFM Request", "Wasn't successful, waiting 30s"));
					}

					await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
				}

				WriteLog(GetLogMessage("I", "LastFM Request", "Got outside, stopping"));
			});

			lastFMThread.Start();
		}

		private void StopLastFMScrobbler()
		{
			isRunning = false;
			statusLabel.Text = "Ready";
		}

		private void SetDiscordStatus(LastTrack track, bool isPlaying)
		{
			bool didInit = false;
			if (!rpcClient.IsInitialized)
			{
				WriteLog(GetLogMessage("I", "Discord RPC", "Not Initialised"));
				if (rpcClient.Initialize())
				{
					WriteLog(GetLogMessage("I", "Discord RPC", "Successfully Initialised"));
					didInit = true;
				}
				else
				{
					WriteLog(GetLogMessage("E", "Discord RPC", "Failure initialising, is Discord running?"));
				}
			}
			else
			{
				didInit = true;
			}

			if (didInit)
			{
				Timestamps stamps = null;

				if (track.TimePlayed.HasValue && track.Duration.HasValue)
				{
					var end = track.TimePlayed.Value.Add(track.Duration.Value);

					stamps = new Timestamps
					{
						EndUnixMilliseconds = Convert.ToUInt64(end.ToUnixTimeMilliseconds())
					};
				}

				rpcClient.SetPresence(new RichPresence
				{
					Assets = new Assets
					{
						LargeImageKey = "lastfm",
						SmallImageKey = isPlaying ? "playing" : "paused"
					},
					Details = $"{track.ArtistName} - {track.Name}",
					Timestamps = stamps
				});

				WriteLog(GetLogMessage("I", "Discord RPC", "Set Status"));
			}
		}

		private void start_Click(object sender, System.EventArgs e)
		{
			discordAppId.ReadOnly = true;
			lastFMKey.ReadOnly = true;
			lastFMSecret.ReadOnly = true;
			lastFMUsername.ReadOnly = true;
			Configure();
			StartLastFMScrobbler();
			start.Enabled = false;
			stop.Enabled = true;
		}

		private void stop_Click(object sender, System.EventArgs e)
		{
			discordAppId.ReadOnly = false;
			lastFMKey.ReadOnly = false;
			lastFMSecret.ReadOnly = false;
			lastFMUsername.ReadOnly = false;
			StopLastFMScrobbler();
			start.Enabled = true;
			stop.Enabled = false;
		}

		public class Config
		{
			public string LastFMKey;
			public string LastFMSecret;
			public string DiscordId;
			public string Username;

			public void Set(string key, string secret, string id, string username)
			{
				LastFMKey = key;
				LastFMSecret = secret;
				DiscordId = id;
				Username = username;
			}
		}
	}
}
