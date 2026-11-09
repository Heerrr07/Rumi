using System;
using System.Drawing;
using System.Globalization;
using System.IO.Ports;
using System.Speech.Recognition;
using System.Windows.Forms;

namespace RumiVoiceOnly
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new VoiceOnlyForm());
        }
    }

    internal sealed class VoiceOnlyForm : Form
    {
        private const string RumiPort = "COM5";
        private const int BaudRate = 115200;
        private const double WakeConfidence = 0.22;

        private readonly Label connectionLabel = new Label();
        private readonly Label stateLabel = new Label();
        private readonly Label instructionLabel = new Label();
        private readonly Label heardLabel = new Label();
        private readonly ProgressBar microphoneLevel = new ProgressBar();
        private readonly Timer reconnectTimer = new Timer();
        private readonly Timer stateTimer = new Timer();

        private SerialPort serial;
        private SpeechRecognitionEngine recognizer;
        private Grammar wakeGrammar;
        private DictationGrammar commandGrammar;
        private bool rumiAwake;
        private DateTime activeUntil;
        private DateTime lastWakeSent = DateTime.MinValue;

        public VoiceOnlyForm()
        {
            Text = "Rumi — Voice Listener";
            ClientSize = new Size(620, 370);
            MinimumSize = new Size(636, 409);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(19, 17, 29);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10F);

            Label title = new Label();
            title.Text = "Rumi is listening";
            title.Font = new Font("Segoe UI Semibold", 24F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(86, 229, 190);
            title.AutoSize = true;
            title.Location = new Point(28, 20);
            Controls.Add(title);

            Label noClick = new Label();
            noClick.Text = "No buttons. Keep this window open or minimized.";
            noClick.AutoSize = true;
            noClick.ForeColor = Color.FromArgb(200, 195, 215);
            noClick.Location = new Point(32, 68);
            Controls.Add(noClick);

            connectionLabel.Text = "Connecting to Rumi on COM5...";
            connectionLabel.AutoSize = true;
            connectionLabel.ForeColor = Color.FromArgb(255, 190, 115);
            connectionLabel.Location = new Point(32, 105);
            Controls.Add(connectionLabel);

            Panel statePanel = new Panel();
            statePanel.Location = new Point(30, 137);
            statePanel.Size = new Size(560, 116);
            statePanel.BackColor = Color.FromArgb(36, 31, 52);
            Controls.Add(statePanel);

            stateLabel.Text = "Rumi is sleeping";
            stateLabel.Font = new Font("Segoe UI Semibold", 19F, FontStyle.Bold);
            stateLabel.ForeColor = Color.FromArgb(157, 148, 194);
            stateLabel.AutoSize = true;
            stateLabel.Location = new Point(20, 16);
            statePanel.Controls.Add(stateLabel);

            instructionLabel.Text = "Starting the laptop microphone...";
            instructionLabel.AutoSize = false;
            instructionLabel.Size = new Size(515, 48);
            instructionLabel.ForeColor = Color.FromArgb(211, 206, 224);
            instructionLabel.Location = new Point(23, 61);
            statePanel.Controls.Add(instructionLabel);

            microphoneLevel.Location = new Point(30, 270);
            microphoneLevel.Size = new Size(560, 13);
            microphoneLevel.Maximum = 100;
            Controls.Add(microphoneLevel);

            heardLabel.Text = "Waiting to hear \"Heyy Rumi\"...";
            heardLabel.AutoEllipsis = true;
            heardLabel.Size = new Size(560, 45);
            heardLabel.ForeColor = Color.FromArgb(180, 175, 197);
            heardLabel.Location = new Point(31, 301);
            Controls.Add(heardLabel);

            reconnectTimer.Interval = 2000;
            reconnectTimer.Tick += delegate { EnsureSerialConnection(); };

            stateTimer.Interval = 250;
            stateTimer.Tick += StateTimerTick;

            Shown += delegate
            {
                StartMicrophone();
                EnsureSerialConnection();
                reconnectTimer.Start();
                stateTimer.Start();
            };

            FormClosing += delegate { ShutDown(); };
        }

        private void StartMicrophone()
        {
            try
            {
                recognizer = new SpeechRecognitionEngine();
                recognizer.SetInputToDefaultAudioDevice();

                Choices phrases = new Choices();
                phrases.Add(new string[] {
                    "Hey Rumi", "Heyy Rumi", "Hi Rumi",
                    "Hey Roomie", "Heyy Roomie", "Hi Roomie",
                    "Hey Roomy", "Hi Roomy",
                    "Hey Romy", "Hi Romy",
                    "Hey Ruby", "Hi Ruby"
                });

                GrammarBuilder builder = new GrammarBuilder();
                builder.Culture = recognizer.RecognizerInfo.Culture;
                builder.Append(phrases);

                wakeGrammar = new Grammar(builder);
                wakeGrammar.Name = "Heyy Rumi";
                wakeGrammar.Priority = 127;
                recognizer.LoadGrammar(wakeGrammar);

                commandGrammar = new DictationGrammar();
                commandGrammar.Name = "Rumi command";
                commandGrammar.Enabled = false;
                recognizer.LoadGrammar(commandGrammar);

                recognizer.AudioLevelUpdated += AudioLevelUpdated;
                recognizer.SpeechHypothesized += SpeechHypothesized;
                recognizer.SpeechRecognized += SpeechRecognized;
                recognizer.SpeechRecognitionRejected += SpeechRejected;
                recognizer.RecognizeAsync(RecognizeMode.Multiple);

                instructionLabel.Text = "Microphone is live. Say \"Heyy Rumi\" to wake her.";
            }
            catch (Exception ex)
            {
                instructionLabel.Text = "Microphone error: " + ex.Message;
                instructionLabel.ForeColor = Color.FromArgb(255, 135, 135);
            }
        }

        private void EnsureSerialConnection()
        {
            if (serial != null && serial.IsOpen) return;

            CloseSerial();
            try
            {
                if (Array.IndexOf(SerialPort.GetPortNames(), RumiPort) < 0)
                {
                    connectionLabel.Text = "Waiting for ESP32 on COM5...";
                    connectionLabel.ForeColor = Color.FromArgb(255, 190, 115);
                    return;
                }

                serial = new SerialPort(RumiPort, BaudRate);
                serial.NewLine = "\n";
                serial.ReadTimeout = 250;
                serial.WriteTimeout = 1000;
                serial.DtrEnable = false;
                serial.RtsEnable = false;
                serial.Open();

                connectionLabel.Text = "ESP32 connected on COM5";
                connectionLabel.ForeColor = Color.FromArgb(86, 229, 190);
            }
            catch (Exception ex)
            {
                CloseSerial();
                connectionLabel.Text = "COM5 unavailable — retrying: " + ex.Message;
                connectionLabel.ForeColor = Color.FromArgb(255, 135, 135);
            }
        }

        private void SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            SafeUi(delegate
            {
                string heard = e.Result.Text.Trim();
                double confidence = e.Result.Confidence;
                heardLabel.Text = string.Format(CultureInfo.InvariantCulture,
                    "Heard: {0}  ({1:0}% confidence)", heard, confidence * 100.0);

                bool isWakeWord = e.Result.Grammar != null &&
                    e.Result.Grammar.Name == "Heyy Rumi";

                if (isWakeWord && confidence >= WakeConfidence)
                {
                    WakeRumi(heard);
                    return;
                }

                if (rumiAwake && confidence >= 0.25)
                {
                    activeUntil = DateTime.Now.AddSeconds(20);
                    instructionLabel.Text = "Listening to your command: \"" + heard + "\"";

                    if ((DateTime.Now - lastWakeSent).TotalSeconds >= 10)
                    {
                        SendCommand("wake");
                        lastWakeSent = DateTime.Now;
                    }
                }
            });
        }

        private void SpeechHypothesized(object sender, SpeechHypothesizedEventArgs e)
        {
            SafeUi(delegate
            {
                if (!rumiAwake)
                {
                    heardLabel.Text = "Hearing: " + e.Result.Text + "...";
                }
            });
        }

        private void SpeechRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            SafeUi(delegate
            {
                if (!rumiAwake)
                {
                    heardLabel.Text = "Waiting to hear \"Heyy Rumi\"...";
                }
            });
        }

        private void AudioLevelUpdated(object sender, AudioLevelUpdatedEventArgs e)
        {
            SafeUi(delegate
            {
                microphoneLevel.Value = Math.Max(0, Math.Min(100, e.AudioLevel));
            });
        }

        private void WakeRumi(string phrase)
        {
            EnsureSerialConnection();
            if (!SendCommand("wake"))
            {
                instructionLabel.Text = "I heard you, but ESP32 is not connected on COM5.";
                return;
            }

            rumiAwake = true;
            activeUntil = DateTime.Now.AddSeconds(20);
            lastWakeSent = DateTime.Now;
            if (commandGrammar != null) commandGrammar.Enabled = true;

            stateLabel.Text = "Rumi is awake!";
            stateLabel.ForeColor = Color.FromArgb(86, 229, 190);
            instructionLabel.ForeColor = Color.FromArgb(211, 206, 224);
            instructionLabel.Text = "Wake phrase detected. Rumi is listening to your command.";
            heardLabel.Text = "Wake phrase: \"" + phrase + "\"";
        }

        private void SleepRumi()
        {
            SendCommand("sleep");
            rumiAwake = false;
            if (commandGrammar != null) commandGrammar.Enabled = false;

            stateLabel.Text = "Rumi is sleeping";
            stateLabel.ForeColor = Color.FromArgb(157, 148, 194);
            instructionLabel.Text = "Microphone is live. Say \"Heyy Rumi\" to wake her.";
            heardLabel.Text = "Waiting to hear \"Heyy Rumi\"...";
        }

        private bool SendCommand(string command)
        {
            try
            {
                if (serial == null || !serial.IsOpen) return false;
                serial.WriteLine(command);
                return true;
            }
            catch
            {
                CloseSerial();
                connectionLabel.Text = "ESP32 disconnected — waiting for COM5...";
                connectionLabel.ForeColor = Color.FromArgb(255, 190, 115);
                return false;
            }
        }

        private void StateTimerTick(object sender, EventArgs e)
        {
            if (!rumiAwake) return;

            double secondsLeft = (activeUntil - DateTime.Now).TotalSeconds;
            if (secondsLeft <= 0)
            {
                SleepRumi();
            }
            else if (!instructionLabel.Text.StartsWith("Listening to your command", StringComparison.Ordinal))
            {
                instructionLabel.Text = "Rumi is listening — sleeping in " +
                    Math.Ceiling(secondsLeft).ToString(CultureInfo.InvariantCulture) + " seconds.";
            }
        }

        private void SafeUi(Action action)
        {
            if (IsDisposed || Disposing) return;
            try { BeginInvoke(action); } catch { }
        }

        private void CloseSerial()
        {
            if (serial == null) return;
            try { if (serial.IsOpen) serial.Close(); } catch { }
            serial.Dispose();
            serial = null;
        }

        private void ShutDown()
        {
            reconnectTimer.Stop();
            stateTimer.Stop();

            if (recognizer != null)
            {
                try { recognizer.RecognizeAsyncCancel(); } catch { }
                try { recognizer.RecognizeAsyncStop(); } catch { }
                recognizer.Dispose();
                recognizer = null;
            }

            if (serial != null && serial.IsOpen)
            {
                try { serial.WriteLine("sleep"); } catch { }
            }
            CloseSerial();
        }
    }
}
