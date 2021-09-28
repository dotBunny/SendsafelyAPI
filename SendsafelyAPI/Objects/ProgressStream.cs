using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using SendSafely.Utilities;
using System.Diagnostics;

namespace SendSafely.Objects
{
    class ProgressStream : Stream
    {
        Stream _inner;
        ISendSafelyProgress _progress;
        String _prefix;
        long _fileSize;
        long _readSoFar;
        int UPDATE_FREQUENCY = 250;
        Stopwatch _stopwatch;
        private double _basePercentage;

        public ProgressStream(Stream inner, ISendSafelyProgress progress, String prefix, long size, double partPercentage)
        {
            this._inner = inner;
            this._progress = progress;
            this._prefix = prefix;
            this._fileSize = size < 1024 ? size * 1024 : size; // multiple file size by 1024 if fileSize is less than 1024 bytes.
            this._readSoFar = 0;
            _stopwatch = new Stopwatch();
            _stopwatch.Start();
            _basePercentage = partPercentage;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);

            _readSoFar += buffer.Length;

            UpdateProgress();
        }

        public override int Read(byte[] buffer, int offset, int count) 
        {
            var result = _inner.Read(buffer, offset, count);

            _readSoFar += result;

            UpdateProgress();
            return result;
        }

        public override bool CanRead
        {
            get { return _inner.CanRead; }
        }

        public override bool CanSeek
        {
            get { return _inner.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return _inner.CanWrite; }
        }

        public override void Close()
        {
            base.Close();
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override long Length
        {
            get { return _inner.Length; }
        }

        public override long Position
        {
            get { return _inner.Position; }
            set { _inner.Position = value; }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _readSoFar = offset;
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _inner.SetLength(value);
        }

        private void UpdateProgress()
        {
            if (_stopwatch.ElapsedMilliseconds > UPDATE_FREQUENCY)
            {
                _stopwatch.Reset();
                _stopwatch.Start();
                _progress.UpdateProgress(_prefix, (_fileSize * _basePercentage + _readSoFar) / _fileSize * 100d);
            }
        }
    }
}
