using Eto.Drawing;
using Eto.Forms;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Scopie
{
    public delegate bool TryParse<T>(string s, out T value);

    public static class UserInterface
    {
        public static Control MakeUi()
        {
            var stack = new StackLayout()
            {
                Orientation = Orientation.Horizontal,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            var mount = Mount.Create();
            if (mount != null)
            {
                stack.Items.Add(MountPanel(mount));
            }
            var numCameras = Camera.NumCameras;
            if (numCameras == 0)
            {
                stack.Items.Add(new Label { Text = "No cameras found" });
            }
            var cameras = Enumerable.Range(0, numCameras).Select(c => new CameraManager(c)).OrderBy(c => c.Name);
            foreach (var camera in cameras)
            {
                var cameraPanel = CameraPanel(camera);
                stack.Items.Add(new StackLayoutItem(cameraPanel, true));
            }
            return stack;
        }

        private static Control MountPanel(Mount mount)
        {
            var stack = new StackLayout()
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MinimumSize = new Size(300, -1),
            };
            stack.Items.Add(new Label { Text = "Mount" });
            var raDec = Settable(new MyTuple<double>(0, 0), async val => await mount.Slew(val.Item1, val.Item2), MyTuple.TryParse);
            var (raDecOne, raDecTwo) = SlewControl(mount);
            stack.Items.Add(raDecOne);
            stack.Items.Add(raDecTwo);
            var (location, setLocation) = Settable(new MyTuple<double>(), async val => await mount.SetLocation(val.Item1, val.Item2), MyTuple.TryParse);
            stack.Items.Add(new Label { Text = "Location" });
            stack.Items.Add(location);
            var (tracking, setTracking) = Settable(Mount.TrackingMode.SiderealPec, async val => await mount.SetTrackingMode(val), Enum.TryParse);
            stack.Items.Add(new Label { Text = "Tracking mode: " + string.Join(", ", (Mount.TrackingMode[])Enum.GetValues(typeof(Mount.TrackingMode))) });
            stack.Items.Add(tracking);

            var timeLabel = new Label { Text = "Mount time" };
            stack.Items.Add(timeLabel);
            async Task SetMountDiff()
            {
                var diff = (await mount.GetTime() - DateTime.Now).ToString();
                timeLabel.Text = "Mount time offset: " + diff;
            }
            stack.Items.Add(new Button(async (sender, args) => await SetMountDiff())
            {
                Text = "Get mount time"
            });
            stack.Items.Add(new Button(async (sender, args) =>
            {
                await mount.SetTime(DateTime.Now);
                await SetMountDiff();
            })
            {
                Text = "Set mount time"
            });

            var init = false;
            stack.Shown += async (sender, args) =>
            {
                if (init)
                {
                    return;
                }
                init = true;
                var (lat, lon) = await mount.GetLocation();
                setLocation(new MyTuple<double>(lat, lon));
                setTracking(await mount.GetTrackingMode());
                await SetMountDiff();
            };

            Button isAligned = null;
            stack.Items.Add(isAligned = new Button(async (sender, args) =>
            {
                isAligned.Text = "Is aligned: " + await mount.IsAligned();
            })
            {
                Text = "Is aligned"
            });

            Button ping = null;
            stack.Items.Add(ping = new Button(async (sender, args) =>
            {
                var start = DateTime.UtcNow;
                var send = start.Millisecond.ToString()[0];
                var res = await mount.Echo(send);
                if (send != res)
                {
                    throw new Exception($"Bad echo! Sent {send}, got {res}");
                }
                var end = DateTime.UtcNow;
                ping.Text = "Ping: " + (end - start).TotalMilliseconds.ToString();
            })
            {
                Text = "Ping"
            });

            return new Scrollable() { Content = stack };
        }

        private static Control CameraPanel(CameraManager camera)
        {
            var exposureConfig = new ExposureConfig();
            var innerStack = new StackLayout()
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ImageDisplay(innerStack, camera, exposureConfig);
            var controls = ControlsUi(camera, exposureConfig);
            ImageControls(controls, exposureConfig);
            Autoguider(controls, camera);
            innerStack.Items.Add(new StackLayoutItem(new Scrollable() { Content = controls }, true));
            return innerStack;
        }

        private static StackLayout ControlsUi(CameraManager camera, ExposureConfig exposure)
        {
            var controlsStack = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            foreach (var control in camera.Controls)
            {
                if (!control.Writeable)
                {
                    continue;
                }
                (Label, Control) toAdd;
                if (control.ControlType == ASICameraDll.ASI_CONTROL_TYPE.ASI_EXPOSURE)
                {
                    toAdd = ExposureControl(control, exposure, 1000000.0);
                }
                else
                {
                    toAdd = SettableControl(control);
                }
                var (lineOne, lineTwo) = toAdd;
                controlsStack.Items.Add(lineOne);
                controlsStack.Items.Add(lineTwo);
            }
            return controlsStack;
        }

        private static void ImageControls(StackLayout layout, ExposureConfig exposure)
        {
            Button cross = null;
            cross = new Button((sender, args) =>
            {
                exposure.Cross = !exposure.Cross;
                cross.Text = "Cross: " + exposure.Cross;
            })
            {
                Text = "Cross: False"
            };
            layout.Items.Add(cross);

            Button zoom = null;
            zoom = new Button((sender, args) =>
            {
                exposure.Zoom = !exposure.Zoom;
                zoom.Text = "Zoom: " + exposure.Zoom;
            })
            {
                Text = "Zoom: False"
            };
            layout.Items.Add(zoom);
        }

        private static void Autoguider(StackLayout layout, CameraManager camera)
        {
            if (!camera.HasST4)
            {
                return;
            }
            var isOn = false;
            Button guider = null;
            guider = new Button((sender, args) =>
            {
                isOn = !isOn;
                guider.Text = "Guider on: " + isOn;
                if (isOn)
                {
                    camera.PulseGuideOn(ASICameraDll.ASI_GUIDE_DIRECTION.ASI_GUIDE_NORTH);
                }
                else
                {
                    camera.PulseGuideOff(ASICameraDll.ASI_GUIDE_DIRECTION.ASI_GUIDE_NORTH);
                }
            })
            {
                Text = "Guider on: False"
            };
            layout.Items.Add(guider);
        }

        private static void ImageDisplay(StackLayout stack, CameraManager camera, ExposureConfig exposureConfig)
        {
            var bitmap = new Bitmap(camera.Width, camera.Height, PixelFormat.Format32bppRgb);
            var image = new ImageView()
            {
                Image = bitmap,
                Height = 400,
            };
            var status = new Label { Text = "Stopped", TextAlignment = TextAlignment.Center };
            Button enableButton = null;
            EventHandler<EventArgs> onEnableClick = async (sender, args) =>
            {
                enableButton.Enabled = false;
                await UserInterfaceControl.RunLoop(camera, bitmap, exposureConfig, status);
                // status.Text = "Stopped";
                enableButton.Enabled = true;
            };
            enableButton = new Button(onEnableClick) { Text = $"Enable: {camera.Name}" };
            var longExposure = LongExposure(exposureConfig);
            stack.Items.Add(image);
            stack.Items.Add(status);
            stack.Items.Add(enableButton);
            stack.Items.Add(longExposure);
            stack.Items.Add(new Button((sender, args) => UserInterfaceControl.Reset(camera, exposureConfig)) { Text = "Reset" });
        }

        private static Control LongExposure(ExposureConfig exposureConfig)
        {
            var longExposureText = new TextBox() { Text = "1.0" };
            var longExposureLabel = new Label();
            exposureConfig.OnChange += () =>
            {
                longExposureLabel.Text = $"Length: {exposureConfig.ExposureLong / 1000000.0}, count: {exposureConfig.CountLong}";
            };
            var longExposureButton = new Button() { Text = "Long exposure" };
            longExposureButton.Click += (sender, args) =>
            {
                if (double.TryParse(longExposureText.Text, out var seconds))
                {
                    exposureConfig.ExposureLong = (int)(seconds * 1000000);
                    exposureConfig.CountLong++;
                }
            };
            var longExposureStack = new StackLayout()
            {
                Orientation = Orientation.Horizontal,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            longExposureStack.Items.Add(new StackLayoutItem(longExposureText, true));
            longExposureStack.Items.Add(longExposureLabel);
            longExposureStack.Items.Add(longExposureButton);
            return longExposureStack;
        }

        private static void SetCameraControl(CameraControl control, int value)
        {
            try
            {
                control.Value = value;
            }
            catch (ASICameraException e)
            {
                MessageBox.Show(e.Message, "ASI Camera Exception", MessageBoxType.Error);
            }
        }

        private static (Label, Control) ExposureControl(CameraControl camera, ExposureConfig exposure, double scale)
        {
            if (camera.ControlType != ASICameraDll.ASI_CONTROL_TYPE.ASI_EXPOSURE)
            {
                throw new Exception($"Non-exposure control passed to ExposureControl: {camera.Name}");
            }
            var description = new Label();
            Action setDescription = () =>
            {
                description.Text = $"{camera.Name} ({camera.MinValue / scale}-{camera.MaxValue / scale} : {camera.DefaultValue / scale}) - actual {camera.Value / scale}";
            };
            setDescription();
            var (control, onChanged) = Settable(() => exposure.ExposureNormal / scale, v => exposure.ExposureNormal = (int)(v * scale), double.TryParse);
            exposure.OnChange += onChanged;
            camera.ValueChanged += setDescription;
            return (description, control);
        }

        private static (Label, Control) SettableControl(CameraControl cameraControl)
        {
            if (!cameraControl.Writeable)
            {
                throw new Exception($"Non-writeable control passed to SettableControl: {cameraControl.Name}");
            }
            var description = new Label() { Text = $"{cameraControl.Name} ({cameraControl.MinValue}-{cameraControl.MaxValue} : {cameraControl.DefaultValue}) - {cameraControl.Description}" };
            var (control, onChanged) = Settable(() => cameraControl.Value, v => SetCameraControl(cameraControl, v), int.TryParse);
            cameraControl.ValueChanged += onChanged;
            return (description, control);
        }

        private static (Control, Action) Settable<T>(Func<T> get, Action<T> set, TryParse<T> tryParse)
        {
            var (control, valueChanged) = Settable(get(), set, tryParse);
            return (control, () => valueChanged(get()));
        }

        private static (Control, Action<T>) Settable<T>(T value, Action<T> set, TryParse<T> tryParse)
        {
            TextBox textBox = null;
            Button controlButton = null;

            Action<T> valueChanged = newValue =>
            {
                value = newValue;
                textBox.Text = value.ToString();
                // TextChanged doesn't fire if strings are already equal
                controlButton.Enabled = false;
            };

            Action setValue = () =>
            {
                if (tryParse(textBox.Text, out var newValue))
                {
                    // will double-fire on set impls that call valueChanged
                    valueChanged(newValue);
                    set(newValue);
                }
            };

            controlButton = new Button((sender, args) => setValue())
            {
                Enabled = false,
                Text = "Set",
            };
            textBox = new TextBox()
            {
                Text = value.ToString(),
            };
            textBox.TextChanged += (sender, args) =>
            {
                var enabled = false;
                if (tryParse(textBox.Text, out var newValue))
                {
                    enabled = !Equals(value, newValue);
                }
                controlButton.Enabled = enabled;
            };
            textBox.KeyDown += (sender, args) =>
            {
                if (args.Key == Keys.Enter)
                {
                    args.Handled = true;
                    setValue();
                }
            };

            var controlStack = new StackLayout()
            {
                Orientation = Orientation.Horizontal,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            controlStack.Items.Add(new StackLayoutItem(textBox, true));
            controlStack.Items.Add(controlButton);
            return (controlStack, valueChanged);
        }

        private static (Control, Control) SlewControl(Mount mount)
        {
            var textBox = new TextBox()
            {
                Text = new MyTuple<double>().ToString(),
            };
            var slewButton = new Button(async (sender, args) =>
            {
                if (MyTuple.TryParse(textBox.Text, out var value))
                {
                    await mount.Slew(value.Item1, value.Item2);
                }
            })
            {
                Text = "Slew",
            };
            var cancelSlewButton = new Button(async (sender, args) =>
            {
                await mount.CancelSlew();
            })
            {
                Text = "Cancel",
            };
            var overwriteButton = new Button(async (sender, args) =>
            {
                if (MyTuple.TryParse(textBox.Text, out var value))
                {
                    await mount.OverwriteRaDec(value.Item1, value.Item2);
                }
            })
            {
                Text = "Overwrite RA/Dec",
            };
            var getButton = new Button(async (sender, args) =>
            {
                var (ra, dec) = await mount.GetRaDec();
                textBox.Text = new MyTuple<double>(ra, dec).ToString("F4");
            })
            {
                Text = "Get RA/Dec",
            };
            textBox.KeyDown += (sender, args) =>
            {
                if (args.Key == Keys.Enter)
                {
                    args.Handled = true;
                    slewButton.PerformClick();
                }
            };

            var lineOne = new StackLayout()
            {
                Orientation = Orientation.Horizontal,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            lineOne.Items.Add(new StackLayoutItem(textBox, true));
            lineOne.Items.Add(slewButton);
            lineOne.Items.Add(cancelSlewButton);

            var lineTwo = new StackLayout()
            {
                Orientation = Orientation.Horizontal,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            lineTwo.Items.Add(new StackLayoutItem(overwriteButton, true));
            lineTwo.Items.Add(new StackLayoutItem(getButton, true));
            return (lineOne, lineTwo);
        }
    }
}
