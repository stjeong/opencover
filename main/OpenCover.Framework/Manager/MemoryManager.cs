﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace OpenCover.Framework.Manager
{
    /// <summary>
    /// Manages the blocks used for communcation and data between host and profiler
    /// </summary>
    public class MemoryManager : IMemoryManager
    {
        private string _namespace;
        private string _key;
        private readonly object _lockObject = new object();

        private readonly IList<Tuple<IManagedCommunicationBlock, IManagedMemoryBlock>> _blocks = new List<Tuple<IManagedCommunicationBlock, IManagedMemoryBlock>>();

        /// <summary>
        /// 
        /// </summary>
        public class ManagedBlock
        {
            protected string Namespace;
            protected string Key;

            /// <summary>
            /// Create a unique name
            /// </summary>
            /// <param name="name"></param>
            /// <param name="id"></param>
            /// <returns></returns>
            protected string MakeName(string name, int id)
            {
                var newName = string.Format("{0}{1}{2}{3}", Namespace, name, Key, id);
                //Console.WriteLine(newName);
                return newName;
            }
        }

        /// <summary>
        /// Contain the memory map and synchronisation objects for result communication
        /// </summary>
        internal class ManagedMemoryBlock : ManagedBlock, IManagedMemoryBlock
        {
            public EventWaitHandle ProfilerHasResults { get; private set; }
            public EventWaitHandle ResultsHaveBeenReceived { get; private set; }
            private readonly MemoryMappedFile _mmfResults;
            public MemoryMappedViewStream StreamAccessorResults { get; private set; }
            public int BufferSize { get; private set; }

            /// <summary>
            /// Gets an ACL for unit test purposes
            /// </summary>
            internal MemoryMappedFileSecurity MemoryAcl
            { 
                get { 
                    return _mmfResults.GetAccessControl(); 
                }
            }

            internal ManagedMemoryBlock(string @namespace, string key, int bufferSize, int bufferId, IEnumerable<string> servicePrincpal)
            {
                Namespace = @namespace;
                Key = key;

                EventWaitHandleSecurity open = null;
                MemoryMappedFileSecurity transparent = null;

                var service = servicePrincpal.FirstOrDefault();
                var currentIdentity = WindowsIdentity.GetCurrent();
                if (service != null && currentIdentity != null)
                {
                    open = new EventWaitHandleSecurity();
                    open.AddAccessRule(new EventWaitHandleAccessRule(currentIdentity.Name, EventWaitHandleRights.FullControl, AccessControlType.Allow));

                    // The event handles need more than just EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize to work
                    open.AddAccessRule(new EventWaitHandleAccessRule(service, EventWaitHandleRights.FullControl, AccessControlType.Allow));

                    transparent = new MemoryMappedFileSecurity();
                    transparent.AddAccessRule(new AccessRule<MemoryMappedFileRights>(currentIdentity.Name, MemoryMappedFileRights.FullControl, AccessControlType.Allow));
                    transparent.AddAccessRule(new AccessRule<MemoryMappedFileRights>(service, MemoryMappedFileRights.ReadWrite, AccessControlType.Allow));
                }

                bool createdNew;

                ProfilerHasResults = new EventWaitHandle(
                    false,
                    EventResetMode.ManualReset, 
                    MakeName(@"\OpenCover_Profiler_Communication_SendResults_Event_", bufferId),
                    out createdNew,
                    open);

                ResultsHaveBeenReceived = new EventWaitHandle(
                    false, 
                    EventResetMode.ManualReset, 
                    MakeName(@"\OpenCover_Profiler_Communication_ReceiveResults_Event_", bufferId),
                    out createdNew,
                    open);

                _mmfResults = MemoryMappedFile.CreateNew(
                    MakeName(@"\OpenCover_Profiler_Results_MemoryMapFile_", bufferId),
                    bufferSize,
                    MemoryMappedFileAccess.ReadWrite,
                    MemoryMappedFileOptions.None,
                    transparent,
                    HandleInheritability.Inheritable);

                StreamAccessorResults = _mmfResults.CreateViewStream(0, bufferSize, MemoryMappedFileAccess.ReadWrite);
                StreamAccessorResults.Write(BitConverter.GetBytes(0), 0, 4);
                BufferSize = bufferSize;
            }

            public void Dispose()
            {
                StreamAccessorResults.Dispose();
                _mmfResults.Dispose();
            }
        }

        /// <summary>
        /// Contain the memory map and synchronisation objects for request communication
        /// </summary>
        internal class ManagedCommunicationBlock : ManagedBlock, IManagedCommunicationBlock
        {
            private readonly MemoryMappedFile _memoryMappedFile;
            public MemoryMappedViewStream StreamAccessorComms { get; private set; }
            public EventWaitHandle ProfilerRequestsInformation { get; private set; }
            public EventWaitHandle InformationReadyForProfiler { get; private set; }
            public EventWaitHandle InformationReadByProfiler { get; private set; }
            public byte[] DataCommunication { get; private set; }
            public GCHandle PinnedDataCommunication { get; private set; }

            /// <summary>
            /// Gets an ACL for unit test purposes
            /// </summary>
            internal MemoryMappedFileSecurity MemoryAcl
            {
                get
                {
                    return _memoryMappedFile.GetAccessControl();
                }
            }

            internal ManagedCommunicationBlock(string @namespace, string key, int bufferSize, int bufferId, IEnumerable<string> servicePrincpal)
            {
                Namespace = @namespace;
                Key = key;

                EventWaitHandleSecurity open = null;
                MemoryMappedFileSecurity transparent = null;

                var service = servicePrincpal.FirstOrDefault();
                var currentIdentity = WindowsIdentity.GetCurrent();
                if (service != null && currentIdentity != null)
                {
                    open = new EventWaitHandleSecurity();
                    open.AddAccessRule(new EventWaitHandleAccessRule(currentIdentity.Name, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                    open.AddAccessRule(new EventWaitHandleAccessRule(service, EventWaitHandleRights.FullControl, AccessControlType.Allow));

                    transparent = new MemoryMappedFileSecurity();
                    transparent.AddAccessRule(new AccessRule<MemoryMappedFileRights>(currentIdentity.Name, MemoryMappedFileRights.FullControl, AccessControlType.Allow));
                    transparent.AddAccessRule(new AccessRule<MemoryMappedFileRights>(service, MemoryMappedFileRights.ReadWrite, AccessControlType.Allow));
                }

                _memoryMappedFile = MemoryMappedFile.CreateNew(
                    MakeName(@"\OpenCover_Profiler_Communication_MemoryMapFile_", bufferId),
                    bufferSize,
                    MemoryMappedFileAccess.ReadWrite,
                    MemoryMappedFileOptions.None,
                    transparent,
                    HandleInheritability.Inheritable);

                StreamAccessorComms = _memoryMappedFile.CreateViewStream(0, bufferSize, MemoryMappedFileAccess.ReadWrite);

                bool createdNew;

                ProfilerRequestsInformation = new EventWaitHandle(
                    false, 
                    EventResetMode.ManualReset,
                    MakeName(@"\OpenCover_Profiler_Communication_SendData_Event_", bufferId),
                    out createdNew,
                    open);

                InformationReadyForProfiler = new EventWaitHandle(
                    false, 
                    EventResetMode.ManualReset,
                    MakeName(@"\OpenCover_Profiler_Communication_ReceiveData_Event_", bufferId),
                    out createdNew,
                    open);

                InformationReadByProfiler = new EventWaitHandle(
                    false, 
                    EventResetMode.ManualReset,
                    MakeName(@"\OpenCover_Profiler_Communication_ChunkData_Event_", bufferId),
                    out createdNew,
                    open);

                DataCommunication = new byte[bufferSize];
                PinnedDataCommunication = GCHandle.Alloc(DataCommunication, GCHandleType.Pinned);
            }

            public void Dispose()
            {
                StreamAccessorComms.Dispose();
                _memoryMappedFile.Dispose();
                PinnedDataCommunication.Free();
            }
        }

        private bool _isIntialised;

        private string[] _servicePrincipal;
        
        /// <summary>
        /// Initialise the memory manager
        /// </summary>
        /// <param name="namespace"></param>
        /// <param name="key"></param>
        /// <param name="servicePrincipal"></param>
        public void Initialise(string @namespace, string key, IEnumerable<string> servicePrincipal)
        {
            if (_isIntialised) return;
            _namespace = @namespace;
            _key = key;
            _servicePrincipal = servicePrincipal.ToArray();
            _isIntialised = true;
        }

        /// <summary>
        /// Allocate a memory buffer
        /// </summary>
        /// <param name="bufferSize"></param>
        /// <param name="bufferId"></param>
        /// <returns></returns>
        public Tuple<IManagedCommunicationBlock, IManagedMemoryBlock> AllocateMemoryBuffer(int bufferSize, uint bufferId)
        {
            if (!_isIntialised) return null;

            lock (_lockObject)
            {
                var tuple = new Tuple<IManagedCommunicationBlock, IManagedMemoryBlock>(
                    new ManagedCommunicationBlock(_namespace, _key, bufferSize, (int)bufferId, _servicePrincipal),
                    new ManagedMemoryBlock(_namespace, _key, bufferSize, (int)bufferId, _servicePrincipal));
                _blocks.Add(tuple);
                return tuple;
            }
        }

        /// <summary>
        /// get a pair of communication+memory blocks
        /// </summary>
        public IList<Tuple<IManagedCommunicationBlock, IManagedMemoryBlock>> GetBlocks
        {
            get { return _blocks; }
        }

        public void Dispose()
        {
            //Console.WriteLine("Disposing...");
            lock (_lockObject)
            {
                foreach(var block in _blocks)
                {
                    block.Item1.Dispose();
                    block.Item2.Dispose();
                }
                _blocks.Clear();
            }
        }
    }
}
