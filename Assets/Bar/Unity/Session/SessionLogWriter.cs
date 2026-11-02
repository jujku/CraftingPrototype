using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using Bar.Domain;
using Bar.Logging;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>把纯日志订阅器接到 UTF-8 文件；文件目录只在宿主中出现。</summary>
    public sealed class SessionLogWriter : IDisposable
    {
        private readonly StreamWriter output;private readonly SessionJournal journal;
        private bool disposed;
        public string FilePath {get;}
        public void Connect(DomainEventStream events)=>journal.Connect(events);
        public void SetAttempt(string id,int attempt){journal.OrderId=id;journal.AttemptIndex=attempt;}
        /// <summary>创建匿名会话日志文件。</summary>
        /// <precondition>events 和 header 有效，directory 可写。</precondition>
        /// <effects>创建随机名文件，连接纯订阅器，发布会话头。</effects>
        public SessionLogWriter(DomainEventStream events,SessionHeader header,string directory)
        {
            Directory.CreateDirectory(directory);FilePath=Path.Combine(directory,"session-"+Guid.NewGuid().ToString("N")+".jsonl");
            output=new StreamWriter(new FileStream(FilePath,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false)){AutoFlush=true};
            journal=new SessionJournal(events,line=>output.WriteLine(line));
            events.Publish(new DomainEvent("session_header",null,null,null,observation:new LogRecord{header=header}));
        }
        /// <summary>计算资源原始字节哈希。</summary>
        /// <precondition>bytes 非 null，name 为不含个人信息的资源名。</precondition>
        /// <effects>返回 SHA-256，不写文件。</effects>
        public static DataHash Hash(string name,byte[] bytes)
        {using(var sha=SHA256.Create())return new DataHash{name=name,sha256=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()};}
        /// <summary>刷新已写日志。</summary>
        /// <precondition>文件尚未 Dispose。</precondition>
        /// <effects>将缓存刷到已打开的文件。</effects>
        public void Flush()=>output.Flush();
        /// <summary>读取仍在写入的会话。</summary>
        /// <precondition>文件尚未 Dispose；在宿主主线程调用。</precondition>
        /// <effects>先刷新，以读写共享读回完整行。</effects>
        public string[] ReadLines()
        {
            Flush();var lines=new List<string>();
            using(var input=new StreamReader(new FileStream(FilePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite),Encoding.UTF8))
            {string line;while((line=input.ReadLine())!=null)lines.Add(line);}
            return lines.ToArray();
        }
        /// <summary>关闭会话文件。</summary>
        /// <precondition>对象仍持有有效文件句柄。</precondition>
        /// <effects>解除领域订阅并关闭文件。</effects>
        public void Dispose(){if(disposed)return;disposed=true;journal.Dispose();output.Dispose();}
    }
}
