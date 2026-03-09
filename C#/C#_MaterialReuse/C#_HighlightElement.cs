// Grasshopper Script Instance
#region Usings
using System;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
#endregion

public class Script_Instance : GH_ScriptInstance
{
    #region Notes
    /* 
      Members:
        RhinoDoc RhinoDocument
        GH_Document GrasshopperDocument
        IGH_Component Component
        int Iteration

      Methods (Virtual & overridable):
        Print(string text)
        Print(string format, params object[] args)
        Reflect(object obj)
        Reflect(object obj, string method_name)
    */
    #endregion

    private void RunScript(List<Line> Line, int PrintWidth, Color Colour)
    {
        to_highlight.Clear();

        foreach (Line l in Line)
        {
            MainGeometry starting_geo = new MainGeometry (l, Colour, PrintWidth);
            to_highlight.Add(starting_geo);
        }
        
    }

    #region Additional
    // <Custom additional code>
    public class MainGeometry
    {
        public Line ObjectGeo;
        public Color ObjectColour;
        public int ObjectWidth;

        public MainGeometry(Line object_geo, Color object_colour, int object_width)
        {
            ObjectGeo = object_geo;
            ObjectColour = object_colour;
            ObjectWidth = object_width;
        }
    }
    
    List<MainGeometry> to_highlight = new List <MainGeometry>();
    // <Custom additional code>
    #endregion

    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        foreach (MainGeometry geo in to_highlight)
        {
            args.Display.DrawLine(geo.ObjectGeo, geo.ObjectColour, geo.ObjectWidth);   
        }
    }


}
