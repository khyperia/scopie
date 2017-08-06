using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Scopie
{
    public delegate bool TryParse<T>(string s, out T value);

    public static class UserInterface
    {
        public static Control MakeUi()
        {
            var stack = new List<Control>();
            var mount = Mount.Create();
            if (mount != null)
            {
                stack.Add(MountPanel(mount));
            }
            var cameras = Enumerable.Range(0, Camera.NumCameras).Select(c => new CameraManager(c)).OrderBy(c => c.Name);
            foreach (var camera in cameras)
            {
                var cameraPanel = CameraPanel(camera);
                stack.Add(cameraPanel);
            }
            if (stack.Count == 0)
            {
                stack.Add(new Label { Text = "No cameras or mount found" });
            }
            return HorizontalExpandAll(stack.ToArray());
        }

        private static Control MountPanel(Mount mount)
        {
            var mountLabel = new Label { Text = "Mount" };
            var raDec = Settable(new MyTuple<Dms>(), async val => await mount.Slew(val.Item1.Value, val.Item2.Value), MyTuple.TryParse);
            var (raDecOne, raDecTwo) = SlewControl(mount);
            var locationOne = new Label { Text = "Location" };
            var (locationTwo, setLocation) = Settable(new MyTuple<Dms>(), async val => await mount.SetLocation(val.Item1.Value, val.Item2.Value), MyTuple.TryParse);
            var trackingOne = new Label { Text = "Tracking mode: " + string.Join(", ", (Mount.TrackingMode[])Enum.GetValues(typeof(Mount.TrackingMode))) };
            var (trackingTwo, setTracking) = Settable(Mount.TrackingMode.SiderealPec, async val => await mount.SetTrackingMode(val), Enum.TryParse);

            var timeLabel = new Label { Text = "Mount time" };
            async Task SetMountDiff()
            {
                var diff = (await mount.GetTime() - DateTime.Now).ToString();
                timeLabel.Text = "Mount time offset: " + diff;
            }
            var getMountTime = new Button(async (sender, args) => await SetMountDiff())
            {
                Text = "Get mount time"
            };
            var setMountTime = new Button(async (sender, args) =>
            {
                await mount.SetTime(DateTime.Now);
                await SetMountDiff();
            })
            {
                Text = "Set mount time"
            };
            var time = HorizontalExpandFirst(timeLabel, getMountTime, setMountTime);

            Button isAligned = null;
            isAligned = new Button(async (sender, args) =>
            {
                isAligned.Text = "Is aligned: " + await mount.IsAligned();
            })
            {
                Text = "Is aligned"
            };

            Button ping = null;
            ping = new Button(async (sender, args) =>
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
            };

            var stack = Vertical(mountLabel, raDecOne, raDecTwo, locationOne, locationTwo, trackingOne, trackingTwo, time, isAligned, ping);

            var init = false;
            stack.Shown += async (sender, args) =>
            {
                if (init)
                {
                    return;
                }
                init = true;
                var (lat, lon) = await mount.GetLocation();
                setLocation(new MyTuple<Dms>(new Dms(lat), new Dms(lon)));
                setTracking(await mount.GetTrackingMode());
                await SetMountDiff();
            };

            return new Scrollable() { Content = stack };
        }

        private static Control CameraPanel(CameraManager camera)
        {
            var exposureConfig = new ExposureConfig();
            var stack = new List<Control>();
            ImageDisplay(stack, camera, exposureConfig);
            var innerStack = new List<Control>();
            ControlsUi(innerStack, camera, exposureConfig);
            SpecialControls(innerStack, camera, exposureConfig);
            stack.Add(new Scrollable() { Content = Vertical(innerStack.ToArray()) });
            return VerticalExpandLast(stack.ToArray());
        }

        private static void ImageDisplay(List<Control> stack, CameraManager camera, ExposureConfig exposureConfig)
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
            stack.Add(image);
            stack.Add(enableButton);
            stack.Add(status);
            stack.Add(longExposure);
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
            return HorizontalExpandFirst(longExposureText, longExposureLabel, longExposureButton);
        }

        private static void ControlsUi(List<Control> controlsStack, CameraManager camera, ExposureConfig exposure)
        {
            controlsStack.Add(new Button((sender, args) => UserInterfaceControl.Reset(camera, exposure)) { Text = "Reset" });
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
                controlsStack.Add(lineOne);
                controlsStack.Add(lineTwo);
            }
        }

        private static void SpecialControls(List<Control> layout, CameraManager camera, ExposureConfig exposure)
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
            layout.Add(cross);

            Button zoom = null;
            zoom = new Button((sender, args) =>
            {
                exposure.Zoom = !exposure.Zoom;
                zoom.Text = "Zoom: " + exposure.Zoom;
            })
            {
                Text = "Zoom: False"
            };
            layout.Add(zoom);
            if (camera.HasST4)
            {
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
                layout.Add(guider);
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
            var (control, onChanged) = Settable(() => cameraControl.Value, v => UserInterfaceControl.SetCameraControl(cameraControl, v), int.TryParse);
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

            var controlStack = HorizontalExpandFirst(textBox, controlButton);
            return (controlStack, valueChanged);
        }

        private static (Control, Control) SlewControl(Mount mount)
        {
            var textBox = new TextBox()
            {
                Text = new MyTuple<Dms>().ToString(),
            };
            var slewButton = new Button(async (sender, args) =>
            {
                if (MyTuple.TryParse(textBox.Text, out var value))
                {
                    await mount.Slew(value.Item1.Value, value.Item2.Value);
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
                    await mount.OverwriteRaDec(value.Item1.Value, value.Item2.Value);
                }
            })
            {
                Text = "Overwrite RA/Dec",
            };
            var currentPos = new Label()
            {
                Text = "Telescope position",
            };
            var shown = false;
            currentPos.Shown += async (sender, args) =>
            {
                if (shown)
                {
                    return;
                }
                shown = true;
                while (true)
                {
                    var (ra, dec) = await mount.GetRaDec();
                    currentPos.Text = new MyTuple<Dms>(new Dms(ra), new Dms(dec)).ToRaDecString();
                    await Task.Delay(2000);
                }
            };
            textBox.KeyDown += (sender, args) =>
            {
                if (args.Key == Keys.Enter)
                {
                    args.Handled = true;
                    slewButton.PerformClick();
                }
            };

            var lineOne = HorizontalExpandFirst(textBox, slewButton, cancelSlewButton);
            var lineTwo = HorizontalExpandAll(overwriteButton, currentPos);
            return (lineOne, lineTwo);
        }

        public static StackLayout Vertical(params Control[] controls)
            => Stack(false, controls.Select(c => new StackLayoutItem(c)));

        public static StackLayout VerticalExpandAll(params Control[] controls)
            => Stack(false, controls.Select(c => new StackLayoutItem(c, true)));

        public static StackLayout VerticalExpandFirst(params Control[] controls)
            => Stack(false, controls.Select((c, i) => new StackLayoutItem(c, i == 0)));

        public static StackLayout VerticalExpandLast(params Control[] controls)
            => Stack(false, controls.Select((c, i) => new StackLayoutItem(c, i == controls.Length - 1)));

        public static StackLayout Horizontal(params Control[] controls)
            => Stack(true, controls.Select(c => new StackLayoutItem(c)));

        public static StackLayout HorizontalExpandAll(params Control[] controls)
            => Stack(true, controls.Select(c => new StackLayoutItem(c, true)));

        public static StackLayout HorizontalExpandFirst(params Control[] controls)
            => Stack(true, controls.Select((c, i) => new StackLayoutItem(c, i == 0)));

        public static StackLayout HorizontalExpandLast(params Control[] controls)
            => Stack(true, controls.Select((c, i) => new StackLayoutItem(c, i == controls.Length - 1)));

        public static StackLayout Stack(bool horizontal, IEnumerable<StackLayoutItem> controls)
        {
            var stack = new StackLayout();
            if (horizontal)
            {
                stack.Orientation = Orientation.Horizontal;
                stack.VerticalContentAlignment = VerticalAlignment.Stretch;
            }
            else
            {
                stack.Orientation = Orientation.Vertical;
                stack.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            };
            foreach (var control in controls)
            {
                stack.Items.Add(control);
            }
            return stack;
        }
    }
}
