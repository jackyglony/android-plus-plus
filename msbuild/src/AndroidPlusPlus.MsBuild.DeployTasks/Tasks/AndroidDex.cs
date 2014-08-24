﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.IO;
using System.Reflection;
using System.Resources;

using Microsoft.Build.Framework;
using Microsoft.Win32;
using Microsoft.Build.Utilities;

using AndroidPlusPlus.MsBuild.Common;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.MsBuild.DeployTasks
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public class AndroidDex : TrackedOutOfDateToolTask, ITask
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public AndroidDex ()
      : base (new ResourceManager ("AndroidPlusPlus.MsBuild.DeployTasks.Properties.Resources", Assembly.GetExecutingAssembly ()))
    {
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    [Required]
    public ITaskItem OutputFile { get; set; }

    [Required]
    public string DexJar { get; set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override int TrackedExecuteTool (string pathToTool, string responseFileCommands, string commandLineCommands)
    {

      int retCode = -1;

      try
      {
        retCode = base.TrackedExecuteTool (pathToTool, responseFileCommands, commandLineCommands);

        if (retCode == 0)
        {
          // 
          // Create a dependency file containing references to each .jar and .class used during execution.
          // 

          using (StreamWriter writer = new StreamWriter (OutputFile.GetMetadata ("FullPath") + ".d", false, Encoding.Unicode))
          {
            writer.WriteLine (string.Format ("{0}: \\", GccUtilities.DependencyParser.ConvertPathWindowsToDependencyFormat (OutputFile.GetMetadata ("FullPath"))));

            foreach (ITaskItem source in Sources)
            {
              string sourceFullPath = source.GetMetadata ("FullPath");

              if (Directory.Exists (sourceFullPath))
              {
                string [] classPathFiles = Directory.GetFiles (sourceFullPath, "*.class", SearchOption.AllDirectories);

                foreach (string classpath in classPathFiles)
                {
                  writer.WriteLine (string.Format ("  {0} \\", GccUtilities.DependencyParser.ConvertPathWindowsToDependencyFormat (classpath)));
                }
              }
              else
              {
                writer.WriteLine (string.Format ("  {0} \\", GccUtilities.DependencyParser.ConvertPathWindowsToDependencyFormat (sourceFullPath)));
              }
            }
          }
        }

        OutputFiles = new ITaskItem [] { OutputFile };
      }
      catch (Exception e)
      {
        Log.LogErrorFromException (e, true);

        retCode = -1;
      }

      return retCode;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override void TrackedExecuteToolOutput (KeyValuePair<string, List<ITaskItem>> commandAndSourceFiles, string singleLine)
    {
      try
      {
        if (!string.IsNullOrWhiteSpace (singleLine))
        {
          if (singleLine.StartsWith ("Exception in thread"))
          {
            Log.LogError (singleLine);
          }
          else
          {
            LogEventsFromTextOutput (string.Format ("[{0}] {1}", ToolName, singleLine), MessageImportance.High);
          }
        }
      }
      catch (Exception e)
      {
        Log.LogErrorFromException (e, true);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override string GenerateCommandLineCommands ()
    {
      // 
      // Build a command-line based on parsing switches from the registered property sheet, and any additional flags.
      // 

      StringBuilder builder = new StringBuilder (PathUtils.CommandLineLength);

      try
      {
        // 
        // JavaVM options need to go at the start of the command line.
        // 

        string jvmInitialHeapSize = m_parsedProperties.ParseProperty (Sources [0], "JvmInitialHeapSize");

        string jvmMaximumHeapSize = m_parsedProperties.ParseProperty (Sources [0], "JvmMaximumHeapSize");

        string jvmThreadStackSize = m_parsedProperties.ParseProperty (Sources [0], "JvmThreadStackSize");

        builder.Append (jvmInitialHeapSize + " ");

        builder.Append (jvmMaximumHeapSize + " ");

        builder.Append (jvmThreadStackSize + " ");

        string frameworkDir = Path.GetDirectoryName (DexJar);

        builder.Append ("-Djava.ext.dirs=\"" + frameworkDir + "\" ");

        builder.Append ("-jar \"" + DexJar + "\" ");

        // 
        // Ensure the JVM options aren't duplicated.
        // 

        StringBuilder parsedProperties = new StringBuilder (m_parsedProperties.Parse (Sources [0]));

        parsedProperties.Replace (jvmInitialHeapSize, "");

        parsedProperties.Replace (jvmMaximumHeapSize, "");

        parsedProperties.Replace (jvmThreadStackSize, "");

        builder.Append (parsedProperties.ToString ());
      }
      catch (Exception e)
      {
        Log.LogErrorFromException (e, true);
      }

      return builder.ToString ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override string ToolName
    {
      get
      {
        return "AndroidDex";
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
