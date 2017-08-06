using System;

namespace Scopie
{
    public class ExposureConfig
    {
        public ExposureConfig()
        {
            _exposureNormal = 500000;
        }

        private int _exposureNormal;
        public int ExposureNormal
        {
            get => _exposureNormal;
            set
            {
                if (_exposureNormal != value)
                {
                    _exposureNormal = value;
                    OnChange?.Invoke();
                }
            }
        }

        private int _exposureLong;
        public int ExposureLong
        {
            get => _exposureLong;
            set
            {
                if (_exposureLong != value)
                {
                    _exposureLong = value;
                    OnChange?.Invoke();
                }
            }
        }

        private int _countLong;
        public int CountLong
        {
            get => _countLong;
            set
            {
                if (_countLong != value)
                {
                    _countLong = value;
                    OnChange?.Invoke();
                }
            }
        }

        public bool Cross
        {
            get; set;
        }

        public bool Zoom
        {
            get; set;
        }

        public event Action OnChange;
    }
}
