﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace sharpmap
{
    public class FileClient : IDisposable
    {
        #region Constants

        private const int DATA_AVAILABLE_OFFSET = 0;
        private const int READ_CONFIRM_OFFSET = DATA_AVAILABLE_OFFSET + 1;
        private const int DATA_LENGTH_OFFSET = READ_CONFIRM_OFFSET + 1;
        private const int DATA_OFFSET = DATA_LENGTH_OFFSET + 10;

        #endregion  


        #region Properties

        public MemoryMappedFile MappedFile { get; set; }
        public event EventHandler<DataReceivedEventArgs> DataReceived;

        public int ReadPosition { get; set; }

        private int writePosition;
        public int WritePosition
        {
            get { return writePosition; }
            set
            {
                if (value != writePosition)
                {
                    writePosition = value;
                    view.Write(WritePosition + READ_CONFIRM_OFFSET, true);
                }
            }
        }

        #endregion


        private MemoryMappedViewAccessor view;
        private AsyncOperation operation;
        private SendOrPostCallback callback;
        private bool started;
        private bool disposed;

        private Thread writerThread;
        private List<byte[]> dataToSend;
        private bool writerThreadRunning;

        public FileClient(string mapName, long cap)
            : this(MemoryMappedFile.CreateOrOpen(mapName, cap), 0, 0, MemoryMappedFileAccess.ReadWrite)
        {
        }

        public FileClient(MemoryMappedFile mappedFile, long offset, long size, MemoryMappedFileAccess access)
        {
            MappedFile = mappedFile;
            view = mappedFile.CreateViewAccessor(offset, size, access);

            ReadPosition = -1;
            writePosition = -1;
            dataToSend = new List<byte[]>();

            callback = new SendOrPostCallback(OnDataReceivedInternal);
            operation = AsyncOperationManager.CreateOperation(null);
        }

        public void StartReader()
        {
            if (started)
                return;

            if (ReadPosition < 0 || writePosition < 0)
                throw new ArgumentException();

            Thread t = new Thread(ReaderThread);
            t.IsBackground = true;
            t.Start();
            started = true;
        }

        public void Write(string message)
        {
            var data = System.Text.Encoding.UTF8.GetBytes(message);
            this.Write(data);
        }

        public void Write(byte[] data)
        {
            if (ReadPosition < 0 || writePosition < 0)
                throw new ArgumentException();

            lock (dataToSend)
                dataToSend.Add(data);

            if (!writerThreadRunning)
            {
                writerThreadRunning = true;
                writerThread = new Thread(WriterThread);
                writerThread.IsBackground = true;
                writerThread.Start();
            }
        }

        public void WriterThread(object stateInfo)
        {
            while (dataToSend.Count > 0 && !disposed)
            {
                byte[] data = null;
                lock (dataToSend)
                {
                    data = dataToSend[0];
                    dataToSend.RemoveAt(0);
                }

                while (!view.ReadBoolean(WritePosition + READ_CONFIRM_OFFSET))
                    Thread.Sleep(500);

                // Sets length and write data.
                view.Write(writePosition + DATA_LENGTH_OFFSET, data.Length);
                view.WriteArray<byte>(writePosition + DATA_OFFSET, data, 0, data.Length);

                // Resets the flag used to signal that data has been read.
                view.Write(writePosition + READ_CONFIRM_OFFSET, false);
                // Sets the flag used to signal that there are data avaibla.
                view.Write(writePosition + DATA_AVAILABLE_OFFSET, true);
            }

            writerThreadRunning = false;
        }

        public void CloseReader()
        {
            started = false;
        }

        private void ReaderThread(object stateInfo)
        {
            while (started)
            {
                // Checks if there is something to read.
                var dataAvailable = view.ReadBoolean(ReadPosition + DATA_AVAILABLE_OFFSET);
                if (dataAvailable)
                {
                    // Checks how many bytes to read.
                    int availableBytes = view.ReadInt32(ReadPosition + DATA_LENGTH_OFFSET);
                    var bytes = new byte[availableBytes];
                    // Reads the byte array.
                    int read = view.ReadArray<byte>(ReadPosition + DATA_OFFSET, bytes, 0, availableBytes);

                    // Sets the flag used to signal that there aren't available data anymore.
                    view.Write(ReadPosition + DATA_AVAILABLE_OFFSET, false);
                    // Sets the flag used to signal that data has been read. 
                    view.Write(ReadPosition + READ_CONFIRM_OFFSET, true);

                    var args = new DataReceivedEventArgs(bytes, read);
                    operation.Post(callback, args);
                }

                Thread.Sleep(500);
            }
        }

        private void OnDataReceivedInternal(object state)
        {
            OnDataReceived(state as DataReceivedEventArgs);
        }

        protected virtual void OnDataReceived(DataReceivedEventArgs e)
        {
            if (e != null && DataReceived != null)
                DataReceived(this, e);
        }

        public void Dispose()
        { 
        }
    }
}
