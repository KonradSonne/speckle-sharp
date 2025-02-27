﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ConnectorGrasshopper.Extras;
using Grasshopper.Kernel;
using GrasshopperAsyncComponent;
using Rhino;
using Speckle.Core.Kits;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Utilities = ConnectorGrasshopper.Extras.Utilities;

namespace ConnectorGrasshopper.Objects
{
  public class ExtendSpeckleObjectAsync : SelectKitAsyncComponentBase, IGH_VariableParameterComponent
  {
    protected override Bitmap Icon => Properties.Resources.ExtendSpeckleObject;

    public override GH_Exposure Exposure => GH_Exposure.hidden;
    
    public override bool Obsolete => true;

    public override Guid ComponentGuid => new Guid("6B1A1705-FDDE-4DE6-9FEA-D31A226F2F66");

    public ExtendSpeckleObjectAsync() : base("Extend Speckle Object", "ESO",
      "Allows you to extend a Speckle object by setting its keys and values.",
      ComponentCategories.PRIMARY_RIBBON, ComponentCategories.OBJECTS)
    {
    }

    public override void SetConverter()
    {
      base.SetConverter();
      BaseWorker = new ExtendSpeckleObjectWorker(this, Converter);
    }

    public override void AddedToDocument(GH_Document document)
    {
      base.AddedToDocument(document); // This would set the converter already.
      BaseWorker = new ExtendSpeckleObjectWorker(this, Converter);
      Params.ParameterNickNameChanged += (sender, args) =>
      {
        args.Parameter.Name = args.Parameter.NickName;
        ExpireSolution(true);
      };
      Params.ParameterChanged += (sender, args) =>
      {
        if (args.OriginalArguments.Type == GH_ObjectEventType.NickName ||
            args.OriginalArguments.Type == GH_ObjectEventType.NickNameAccepted)
        {
          args.Parameter.Name = args.Parameter.NickName;
          ExpireSolution(true);
        }
      };
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      var pObj = pManager.AddParameter(new SpeckleBaseParam("Speckle Object", "O",
        "Speckle object to deconstruct into it's properties.", GH_ParamAccess.item));
      // All other inputs are dynamically generated by the user.
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddParameter(new SpeckleBaseParam("Speckle Object", "O", "Created speckle object", GH_ParamAccess.item));
    }

    public bool CanInsertParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input && index != 0;

    public bool CanRemoveParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input && index != 0;

    public IGH_Param CreateParameter(GH_ParameterSide side, int index)
    {
      var myParam = new GenericAccessParam
      {
        Name = GH_ComponentParamServer.InventUniqueNickname("ABCD", Params.Input),
        MutableNickName = true,
        Optional = true
      };

      myParam.NickName = myParam.Name;
      myParam.Optional = false;
      myParam.ObjectChanged += (sender, e) => { };
      return myParam;
    }

    public bool DestroyParameter(GH_ParameterSide side, int index)
    {
      return true;
    }

    public void VariableParameterMaintenance()
    {
    }
  }

  public class ExtendSpeckleObjectWorker : WorkerInstance
  {
    public Base @base;
    public ISpeckleConverter Converter;
    private Dictionary<string, object> inputData;

    public ExtendSpeckleObjectWorker(GH_Component parent, ISpeckleConverter converter) : base(parent)
    {
      Converter = converter;
      inputData = new Dictionary<string, object>();
    }

    public override WorkerInstance Duplicate() => new ExtendSpeckleObjectWorker(Parent, Converter);

    public override void DoWork(Action<string, double> ReportProgress, Action Done)
    {
      try
      {
        Parent.Message = "Creating...";
        var hasErrors = false;

        inputData?.Keys.ToList().ForEach(key =>
        {
          var value = inputData[key];


          if (value is List<object> list)
          {
            // Value is a list of items, iterate and convert.
            List<object> converted = null;
            try
            {
              converted = list.Select(item =>
              {
                return Converter != null ? Utilities.TryConvertItemToSpeckle(item, Converter) : item;
              }).ToList();
            }
            catch (Exception e)
            {
              Log.CaptureException(e);
              RuntimeMessages.Add((GH_RuntimeMessageLevel.Warning, $"{e.Message}"));
              hasErrors = true;
            }

            try
            {
              @base[key] = converted;
            }
            catch (Exception e)
            {
              Log.CaptureException(e);
              RuntimeMessages.Add((GH_RuntimeMessageLevel.Error, $"{e.Message}"));
              hasErrors = true;
            }
          }
          else
          {
            // If value is not list, it is a single item.

            try
            {
              if (Converter != null)
                @base[key] = value == null ? null : Utilities.TryConvertItemToSpeckle(value, Converter);
              else
                @base[key] = value;
            }
            catch (Exception e)
            {
              Log.CaptureException(e);
              RuntimeMessages.Add((GH_RuntimeMessageLevel.Error, $"{e.Message}"));
              hasErrors = true;
            }
          }
        });

        if (hasErrors)
        {
          @base = null;
        }
      }
      catch (Exception e)
      {
        // If we reach this, something happened that we weren't expecting...
        Log.CaptureException(e);
        RuntimeMessages.Add((GH_RuntimeMessageLevel.Error, "Something went terribly wrong... " + e.Message));
        Parent.Message = "Error";
      }

      // Let's always call done!
      Done();
    }

    List<(GH_RuntimeMessageLevel, string)> RuntimeMessages { get; set; } = new List<(GH_RuntimeMessageLevel, string)>();

    public override void SetData(IGH_DataAccess DA)
    {
      // 👉 Checking for cancellation!
      if (CancellationToken.IsCancellationRequested) return;

      // Report all conversion errors as warnings
      if (Converter != null)
        foreach (var error in Converter.Report.ConversionErrors)
        {
          Parent.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
            error.Message + ": " + error.InnerException?.Message);
        }

      foreach (var (level, message) in RuntimeMessages)
      {
        Parent.AddRuntimeMessage(level, message);
      }

      if (@base != null)
        DA.SetData(0, new GH_SpeckleBase {Value = @base});
    }

    public Base targetObject;

    public override void GetData(IGH_DataAccess DA, GH_ComponentParamServer Params)
    {
      DA.DisableGapLogic();
      GH_SpeckleBase speckleBase = null;
      DA.GetData(0, ref speckleBase);
      @base = speckleBase.Value.ShallowCopy();
      if (Params.Input.Count == 1)
      {
        inputData = null;
        return;
      }

      var hasErrors = false;
      var allOptional = Params.Input.FindAll(p => p.Optional).Count == Params.Input.Count;
      if (Params.Input.Count > 1 && allOptional)
      {
        RuntimeMessages.Add((GH_RuntimeMessageLevel.Error, "You cannot set all parameters as optional"));
        inputData = null;
        return;
      }

      inputData = new Dictionary<string, object>();
      for (int i = 1; i < Params.Input.Count; i++)
      {
        var ighParam = Params.Input[i];
        var param = ighParam as GenericAccessParam;
        var index = Params.IndexOfInputParam(param.Name);
        var detachable = param.Detachable;
        var key = detachable ? "@" + param.NickName : param.NickName;

        var willOverwrite = @base.GetMembers().ContainsKey(key);
        var targetIndex = DA.ParameterTargetIndex(0);
        var path = DA.ParameterTargetPath(0);
        if (willOverwrite)
          RuntimeMessages.Add((GH_RuntimeMessageLevel.Remark,
            $"Key {key} already exists in object at {path}[{targetIndex}], its value will be overwritten"));
        switch (param.Access)
        {
          case GH_ParamAccess.item:
            object value = null;
            DA.GetData(index, ref value);
            if (!param.Optional && value == null)
            {
              RuntimeMessages.Add((GH_RuntimeMessageLevel.Warning,
                $"Non-optional parameter {param.NickName} cannot be null"));
              hasErrors = true;
            }

            inputData[key] = value;
            break;
          case GH_ParamAccess.list:
            var values = new List<object>();
            DA.GetDataList(index, values);
            if (!param.Optional)
            {
              if (values.Count == 0)
              {
                RuntimeMessages.Add((GH_RuntimeMessageLevel.Warning,
                  $"Non-optional parameter {param.NickName} cannot be null or empty."));
                hasErrors = true;
              }
            }

            inputData[key] = values;
            break;
          case GH_ParamAccess.tree:
            break;
          default:
            throw new ArgumentOutOfRangeException();
        }
      }

      if (hasErrors) inputData = null;
    }
  }
}
