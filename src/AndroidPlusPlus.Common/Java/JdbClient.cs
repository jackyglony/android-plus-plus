﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.Common
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public class JdbClient : AsyncRedirectProcess.EventListener, IDisposable
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public enum StepType
    {
      Statement,
      Line,
      Instruction
    };

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public delegate void OnAsyncOutputDelegate (string [] output);

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public OnAsyncOutputDelegate OnAsyncStdout { get; set; }

    public OnAsyncOutputDelegate OnAsyncStderr { get; set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private class AsyncCommandData
    {
      public AsyncCommandData ()
      {
      }

      public string Command { get; set; }

      public List<string> OutputLines = new List <string> ();

      public OnAsyncOutputDelegate OutputDelegate { get; set; }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private readonly JdbSetup m_jdbSetup;

    private AsyncRedirectProcess m_jdbClientInstance = null;

    private Dictionary<uint, AsyncCommandData> m_asyncCommandData = new Dictionary<uint, AsyncCommandData> ();

    private Dictionary<string, ManualResetEvent> m_syncCommandLocks = new Dictionary<string, ManualResetEvent> ();

    private Stopwatch m_timeSinceLastOperation = new Stopwatch ();

    private ManualResetEvent m_sessionStarted = new ManualResetEvent (false);

    private uint m_sessionCommandToken = 1;

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public JdbClient (JdbSetup jdbSetup)
    {
      LoggingUtils.PrintFunction ();

      m_jdbSetup = jdbSetup;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Kill ()
    {
      LoggingUtils.PrintFunction ();

      try
      {
        if (m_jdbClientInstance != null)
        {
          m_jdbClientInstance.Kill ();
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose ()
    {
      LoggingUtils.PrintFunction ();

      try
      {
        if (m_jdbClientInstance != null)
        {
          m_jdbClientInstance.Dispose ();

          m_jdbClientInstance = null;
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Start ()
    {
      LoggingUtils.PrintFunction ();

      // 
      // Export an execution script ('jdb.ini') for standard start-up properties.
      // 

      string [] execCommands = m_jdbSetup.CreateJdbExecutionScript ();

      using (StreamWriter writer = new StreamWriter (Path.Combine (m_jdbSetup.CacheDirectory, "jdb.ini"), false, Encoding.ASCII))
      {
        foreach (string command in execCommands)
        {
          writer.WriteLine (command);
        }

        writer.Close ();
      }

      // 
      // Prepare a new JDB instance. Connections must be made on the command line, so delay this until an attach request.
      // 

      StringBuilder argumentBuilder = new StringBuilder ();

      argumentBuilder.Append (string.Format (" -connect com.sun.jdi.SocketAttach:hostname={0},port={1}", m_jdbSetup.Host, m_jdbSetup.Port));

      m_jdbClientInstance = new AsyncRedirectProcess (Path.Combine (JavaSettings.JdkRoot, @"bin\jdb.exe"), argumentBuilder.ToString ());
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Stop ()
    {
      LoggingUtils.PrintFunction ();

      Kill ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Attach ()
    {
      LoggingUtils.PrintFunction ();

      try
      {
        m_jdbSetup.ClearPortForwarding ();

        m_jdbSetup.SetupPortForwarding ();

        m_jdbClientInstance.Start (this);

        m_timeSinceLastOperation.Start ();

        /*uint timeout = 15000;

        bool responseSignaled = false;

        while ((!responseSignaled) && (m_timeSinceLastOperation.ElapsedMilliseconds < timeout))
        {
          responseSignaled = m_sessionStarted.WaitOne (0);

          if (!responseSignaled)
          {
            Thread.Yield ();
          }
        }

        if (!responseSignaled)
        {
          throw new TimeoutException ("Timed out waiting for JDB client to execute/attach");
        }*/
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        throw;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Detach ()
    {
      LoggingUtils.PrintFunction ();

      SendCommand ("exit");
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Interrupt ()
    {
      LoggingUtils.PrintFunction ();

      SendCommand ("interrupt 0");
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Continue ()
    {
      LoggingUtils.PrintFunction ();

      SendCommand ("cont");
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Terminate ()
    {
      LoggingUtils.PrintFunction ();

      SendAsyncCommand ("exit");
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void StepInto (uint threadId, StepType stepType, bool reverse)
    {
      LoggingUtils.PrintFunction ();

      switch (stepType)
      {
        case StepType.Statement:
        case StepType.Line:
        {
          string command = "step";

          SendCommand (command);

          break;
        }

        case StepType.Instruction:
        {
          string command = "stepi";

          SendCommand (command);

          break;
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void StepOut (uint threadId, StepType stepType, bool reverse)
    {
      LoggingUtils.PrintFunction ();

      switch (stepType)
      {
        case StepType.Statement:
        case StepType.Line:
        case StepType.Instruction:
        {
          string command = "step up";

          SendCommand (command);

          break;
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void StepOver (uint threadId, StepType stepType, bool reverse)
    {
      LoggingUtils.PrintFunction ();

      switch (stepType)
      {
        case StepType.Statement:
        case StepType.Line:
        case StepType.Instruction:
        {
          string command = "next";

          SendCommand (command);

          break;
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public string [] SendCommand (string command, int timeout = 30000)
    {
      // 
      // Perform a synchronous command request.
      // 

      LoggingUtils.Print (string.Format ("[JdbClient] SendCommand: {0}", command));

      if (string.IsNullOrWhiteSpace (command))
      {
        throw new ArgumentNullException ("command");
      }

      string [] syncOutput = null;

      if (m_jdbClientInstance == null)
      {
        return syncOutput;
      }

      ManualResetEvent syncCommandLock = new ManualResetEvent (false);

      m_syncCommandLocks [command] = syncCommandLock;

      SendAsyncCommand (command, delegate (string [] output)
      {
        syncOutput = output;

        syncCommandLock.Set ();
      });

      // 
      // Wait for asynchronous record response (or exit), reset timeout each time new activity occurs.
      // 

      /*bool responseSignaled = false;

      while ((!responseSignaled) && (m_timeSinceLastOperation.ElapsedMilliseconds < timeout))
      {
        responseSignaled = syncCommandLock.WaitOne (0);

        if (!responseSignaled)
        {
          Thread.Yield ();
        }
      }*/

      m_syncCommandLocks.Remove (command);

      /*if (!responseSignaled)
      {
        throw new TimeoutException ("Timed out waiting for synchronous response for command: " + command);
      }*/

      return syncOutput;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void SendAsyncCommand (string command, OnAsyncOutputDelegate asyncDelegate = null)
    {
      LoggingUtils.Print (string.Format ("[JdbClient] SendAsyncCommand: {0}", command));

      if (string.IsNullOrWhiteSpace (command))
      {
        throw new ArgumentNullException ("command");
      }

      if (m_jdbClientInstance == null)
      {
        return;
      }

      m_timeSinceLastOperation.Restart ();

      AsyncCommandData commandData = new AsyncCommandData ();

      commandData.Command = command;

      commandData.OutputDelegate = asyncDelegate;

      ++m_sessionCommandToken;

      lock (m_asyncCommandData)
      {
        m_asyncCommandData.Add (m_sessionCommandToken, commandData);
      }

      //command = m_sessionCommandToken + command;

      m_jdbClientInstance.SendCommand (command);

      m_timeSinceLastOperation.Restart ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void ProcessStdout (object sendingProcess, DataReceivedEventArgs args)
    {
      if (!string.IsNullOrEmpty (args.Data))
      {
        LoggingUtils.Print (string.Format ("[JdbClient] ProcessStdout: {0}", args.Data));

        try
        {
          m_timeSinceLastOperation.Restart ();

          if (args.Data.Equals ("Initializing jdb ..."))
          {
            m_sessionStarted.Set ();
          }

          // 
          // Distribute result records to registered delegate callbacks.
          // 

          OnAsyncStdout (new string [] { args.Data });

          // 
          // Collate output for any ongoing async commands.
          // 

          lock (m_asyncCommandData)
          {
            foreach (KeyValuePair<uint, AsyncCommandData> asyncCommand in m_asyncCommandData)
            {
              if (!asyncCommand.Value.Command.StartsWith ("-"))
              {
                asyncCommand.Value.OutputLines.Add (args.Data);
              }
            }
          }

          // 
          // Call the corresponding registered delegate for the token response.
          // 

          uint token = m_sessionCommandToken;

          AsyncCommandData callbackCommandData = null;

          lock (m_asyncCommandData)
          {
            if (m_asyncCommandData.TryGetValue (token, out callbackCommandData))
            {
              m_asyncCommandData.Remove (token);
            }
          }

          // 
          // Spawn any registered callback handlers on a dedicated thread, as not to block JDB output.
          // 

          if ((callbackCommandData != null) && (callbackCommandData.OutputDelegate != null))
          {
            ThreadPool.QueueUserWorkItem (delegate (object state)
            {
              try
              {
                callbackCommandData.OutputDelegate (callbackCommandData.OutputLines.ToArray ());
              }
              catch (Exception e)
              {
                LoggingUtils.HandleException (e);
              }
            });
          }
        }
        catch (Exception e)
        {
          LoggingUtils.HandleException (e);
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void ProcessStderr (object sendingProcess, DataReceivedEventArgs args)
    {
      try
      {
        m_timeSinceLastOperation.Restart ();

        if (!string.IsNullOrWhiteSpace (args.Data))
        {
          LoggingUtils.Print (string.Format ("[JdbClient] ProcessStderr: {0}", args.Data));

          OnAsyncStderr (new string [] { args.Data });
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void ProcessExited (object sendingProcess, EventArgs args)
    {
      try
      {
        m_timeSinceLastOperation.Restart ();

        LoggingUtils.Print (string.Format ("[JdbClient] ProcessExited"));

        m_jdbClientInstance = null;

        // 
        // If we're waiting on a synchronous command, signal a finish to process termination.
        // 

        foreach (KeyValuePair<string, ManualResetEvent> syncKeyPair in m_syncCommandLocks)
        {
          syncKeyPair.Value.Set ();
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  }

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
