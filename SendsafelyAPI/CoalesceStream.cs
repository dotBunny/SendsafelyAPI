﻿/*

Boost Software License - Version 1.0 - August 17th, 2003

Permission is hereby granted, free of charge, to any person or organization
obtaining a copy of the software and accompanying documentation covered by
this license (the "Software") to use, reproduce, display, distribute,
execute, and transmit the Software, and to prepare derivative works of the
Software, and to permit third-parties to whom the Software is furnished to
do so, all subject to the following:

The copyright notices in the Software and this entire statement, including
the above license grant, this restriction and the following disclaimer,
must be included in all copies of the Software, in whole or in part, and
all derivative works of the Software, unless such copies or derivative
works are solely in the form of machine-executable object code generated by
a source language processor.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT
SHALL THE COPYRIGHT HOLDERS OR ANYONE DISTRIBUTING THE SOFTWARE BE LIABLE
FOR ANY DAMAGES OR OTHER LIABILITY, WHETHER IN CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.

Copyright (c) 2020-2021 dotBunny Inc.
dotBunny licenses this file to you under the BSL-1.0 license.
https://github.com/dotBunny/K9/blob/main/K9.Core/IO/CoalesceStream.cs

*/

using System;
using System.Collections.Generic;
using System.IO;


namespace SendSafely
{
    public class CoalesceStream : Stream
    {
        private readonly List<byte[]> _blocks = new();

        private readonly long blockSize = 65536;

        private long _lengthInternal;

        public CoalesceStream()
        {
            Position = 0;
        }

        public CoalesceStream(byte[] source)
        {
            Write(source, 0, source.Length);
            Position = 0;
        }

        public CoalesceStream(int length)
        {
            SetLength(length);
            Position = length;
            byte[] d = Block;
            Position = 0;
        }

        public CoalesceStream(long length)
        {
            SetLength(length);
            Position = length;
            byte[] d = Block;
            Position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _lengthInternal;
        public sealed override long Position { get; set; }

        private byte[] Block
        {
            get
            {
                while (_blocks.Count <= BlockId)
                {
                    _blocks.Add(new byte[blockSize]);
                }

                return _blocks[(int)BlockId];
            }
        }

        private long BlockId => Position / blockSize;

        private long BlockOffset => Position % blockSize;


        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long readCount = count;

            if (readCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), readCount,
                    "Number of bytes to copy cannot be negative.");
            }

            long remaining = _lengthInternal - Position;
            if (readCount > remaining)
            {
                readCount = remaining;
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer), "Buffer cannot be null.");
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, "Destination offset cannot be negative.");
            }

            int read = 0;
            do
            {
                long copySize = Math.Min(readCount, blockSize - BlockOffset);
                Buffer.BlockCopy(Block, (int)BlockOffset, buffer, offset, (int)copySize);
                readCount -= copySize;
                offset += (int)copySize;

                read += (int)copySize;
                Position += copySize;
            } while (readCount > 0);

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.End:
                    Position = Length - offset;
                    break;
            }

            return Position;
        }

        public sealed override void SetLength(long value)
        {
            _lengthInternal = value;
        }

        public sealed override void Write(byte[] buffer, int offset, int count)
        {
            long initialPosition = Position;
            try
            {
                do
                {
                    int copySize = Math.Min(count, (int)(blockSize - BlockOffset));

                    EnsureCapacity(Position + copySize);

                    Buffer.BlockCopy(buffer, offset, Block, (int)BlockOffset, copySize);
                    count -= copySize;
                    offset += copySize;

                    Position += copySize;
                } while (count > 0);
            }
            catch (Exception)
            {
                Position = initialPosition;
                throw;
            }
        }

        public override int ReadByte()
        {
            if (Position >= _lengthInternal)
            {
                return -1;
            }

            byte b = Block[BlockOffset];
            Position++;

            return b;
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacity(Position + 1);
            Block[BlockOffset] = value;
            Position++;
        }

        private void EnsureCapacity(long intendedLength)
        {
            if (intendedLength > _lengthInternal)
            {
                _lengthInternal = intendedLength;
            }
        }
    }
}