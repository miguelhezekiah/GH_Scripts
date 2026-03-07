// Grasshopper Script Instance
#region Usings
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

using Rhino;
using Rhino.Input;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
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

    private void RunScript(
		List<double> ElementLength,
		List<Line> Element,
		bool Run,
		bool Reset,
		ref object UsedMembers,
		ref object UnusedMembers,
		ref object Tags)
    {
        
    if (Reset)
    {
    // Wipe the memory clean
    used.Clear();
    unused.Clear();
    tags.Clear();
    if (hitboxes != null) hitboxes.Clear();
    
    active_target = -1;
    show_ui = false;
    
    // Shut down the sensor
    if (mouse_sensor != null)
    {
        mouse_sensor.Enabled = false;
        mouse_sensor = null;
    }
    
    // Exit the script immediately after resetting
    return; 
    }    


    if (Run)
    {
        used.Clear();
        unused.Clear();
        tags.Clear();
        hitboxes = new List<Box>();

        List<bool> available = Enumerable.Repeat(true, ElementLength.Count).ToList();
        ElementLength.Sort();

        foreach (Line m in Element)
        {
            bool found = false;

            for(int i = 0; i < ElementLength.Count; i++)
            {
                // Assign Elements
                if (available[i] && ElementLength[i] >= m.Length) 
                {
                    available[i] = false;
                    used.Add(m);
                    Point3d start_point = m.PointAt(0);
                    Point3d end_point = m.PointAt(1);

                    double cut = ElementLength[i] - m.Length;
                    tags.Add($"From Database Number: {i}\nLength of Member in Structure: {m.Length:F2} m\nCut: {cut:F2} m\nStart Point: {start_point:F2}\nEnd Point: {end_point:F2}");

                    // Construct Bounding Box
                    Plane beam_plane = new Plane(m.PointAt(0.5), m.Direction);

                    double t = 0.5; 
                    double half_L = m.Length / 2.0;

                    // Construct the mathematical Oriented Box
                    Rhino.Geometry.Box bbox = new Rhino.Geometry.Box
                    (
                        beam_plane, 
                        new Interval(-t, t),    // X thickness
                        new Interval(-t, t),    // Y thickness
                        new Interval(-half_L, half_L) // Z length
                    );

                    hitboxes.Add(bbox);

                    found = true;
                    break;
                }
            }

        }
   
        for (int i = 0; i < ElementLength.Count; i++)
        {
            if (available[i] == true)
            {
                unused.Add($"ID: {i} | Length: {ElementLength[i]} m");
            }

        }

        // Turning on the sensor
        if (mouse_sensor != null)
        {
            mouse_sensor.Enabled = false;
            mouse_sensor = null; 
        }

        mouse_sensor = new Sensor();
        mouse_sensor.Owner = this;
        mouse_sensor.Enabled = true;

        GrasshopperDocument.ObjectsDeleted += KillSwitchEvent;

        
    }
        // Output
        UsedMembers = used;
        UnusedMembers = unused;
        Tags = tags;

        // Redraw
        RhinoDoc.ActiveDoc.Views.Redraw();
    }


    #region Additional
    // <Custom additional code> 

    // Initialize
    List<Line> used = new List<Line>();
    List<string> unused = new List<string>();
    List<string> tags = new List<string>();

    List<Mesh> diagrams = new List<Mesh>();
    List<Line> edges = new List<Line>();
    List<Box> hitboxes;
    int active_target = 0;

    static Sensor mouse_sensor;

    // UI State
    bool show_ui = false;
    int ui_x = 0;
    int ui_y = 0;
    bool is_dragging = false;
    int drag_offset_x = 0;
    int drag_offset_y = 0;

    int ui_width = 220; // Made slightly wider to fit the new coordinate text
    int ui_height = 130;

    // Classes
    void KillSwitchEvent(object sender, GH_DocObjectEventArgs e)
    {
        if(e.Objects.Contains(Component))
        {
            if (mouse_sensor != null)
            {
                mouse_sensor.Enabled = false;
                mouse_sensor = null;
            }
            GrasshopperDocument.ObjectsDeleted -= KillSwitchEvent;
        }
    }

    class Sensor : Rhino.UI.MouseCallback
    {
        public Script_Instance Owner;

        protected override void OnMouseDown(Rhino.UI.MouseCallbackEventArgs e)
        {
            // Avoid errors
            if (Owner == null) return;
            if (e.Button != System.Windows.Forms.MouseButtons.Left) return;

            int click_x = (int)e.ViewportPoint.X;
            int click_y = (int)e.ViewportPoint.Y;
            
            // If user clicks inside the UI window
            if (Owner.show_ui && click_x >= Owner.ui_x && click_x <= Owner.ui_x + Owner.ui_width && click_y >= Owner.ui_y && click_y <= Owner.ui_y + Owner.ui_height)
            {
                Owner.is_dragging = true;
                Owner.drag_offset_x = click_x - Owner.ui_x;
                Owner.drag_offset_y = click_y - Owner.ui_y;

                e.Cancel = true;
                return;
            }

            // Else
            Line laser;
            e.View.ActiveViewport.GetFrustumLine(e.ViewportPoint.X, e.ViewportPoint.Y, out laser);

            int new_target = -1;
            double closest_distance = double.MaxValue;

            for (int i = 0; i < Owner.hitboxes.Count; i++)
            {
                Rhino.Geometry.Interval hit;

                if (Rhino.Geometry.Intersect.Intersection.LineBox(laser, Owner.hitboxes[i], 0.01, out hit))
                {
                    if (hit.T0 < closest_distance)
                    {
                        closest_distance = hit.T0;
                        new_target = i;
                    }
                }
            }

            if (new_target != -1)
            {
                Owner.active_target = new_target;
                Owner.show_ui = true;

                Owner.ui_x = click_x + 20;
                Owner.ui_y = click_y - 20;
            }

            else
            {
                Owner.show_ui = false;
            }

            e.View.Redraw();
        }

        protected override void OnMouseMove(Rhino.UI.MouseCallbackEventArgs e)
        {
            if (Owner ==  null || !Owner.is_dragging) return;

            Owner.ui_x = (int)e.ViewportPoint.X - Owner.drag_offset_x;
            Owner.ui_y = (int)e.ViewportPoint.Y - Owner.drag_offset_y;

            e.View.Redraw();
        }

        protected override void OnMouseUp(Rhino.UI.MouseCallbackEventArgs e)
        {
            if (Owner != null && Owner.is_dragging)
            {
                Owner.is_dragging = false;
            }
        }
    } 


    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        try
        {
            if (used == null) return;

            if (active_target >= 0 && active_target < used.Count && active_target < tags.Count)
            {
                // Highlight Selected Element
                Line selected_element = used[active_target];
                args.Display.DrawLine(selected_element, Color.FromArgb(255, 200, 0), 4);
            }

            if (show_ui && active_target >= 0 && active_target < tags.Count)
            {
                int x = ui_x;
                int y = ui_y;
                int header_h = 25;
                                            
                // Draw Main Background (White Body)
                System.Drawing.Rectangle body = new System.Drawing.Rectangle(x, y + header_h, ui_width, ui_height - header_h);
                args.Display.Draw2dRectangle(body, Color.Empty, 0, Color.FromArgb(30, 30, 30));
                
                // Draw Header Bar (Dark Gray)
                System.Drawing.Rectangle header = new System.Drawing.Rectangle(x, y, ui_width, header_h);
                args.Display.Draw2dRectangle(header, Color.Empty, 0, Color.Black);
                
                // Draw Outer Border (Thin Light Gray)
                System.Drawing.Rectangle border = new System.Drawing.Rectangle(x, y, ui_width, ui_height);
                args.Display.Draw2dRectangle(border, Color.FromArgb(30, 30, 30), 2, Color.Empty);
                
                // Draw Header Text (White, Bold-ish)
                string header_text = $"BEAM NUMBER : {active_target}";
                Point2d header_pos = new Point2d(x + 10, y + 5);
                args.Display.Draw2dText(header_text, Color.FromArgb(175, 175, 175), header_pos, false, 12);
                
                // Draw Body Text
                Point2d body_pos = new Point2d(x + 10, y + header_h + 10);
                string body_text = "";
                if (tags != null && active_target < tags.Count)
                {
                    body_text = tags[active_target];
                }

                args.Display.Draw2dText(body_text, Color.FromArgb(175, 175, 175), body_pos, false, 12);
            }
        }
        catch (Exception ex)
        {
            // Print Error in the Rhino Command Line
            Rhino.RhinoApp.WriteLine("Error: " + ex.Message);
        }
    }

    // <Custom additional code> 
    #endregion
}
