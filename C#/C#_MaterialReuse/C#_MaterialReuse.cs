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
    assign.Clear();
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
        assign.Clear();

        ElementLength.Sort();
        List<bool> available = Enumerable.Repeat(true, ElementLength.Count).ToList();
        List<Line> elements_sorted = Element.OrderByDescending(m => m.Length).ToList();

        double total_part_length = 0;
        double total_stock_used = 0;

        foreach (Line m in elements_sorted)
        {
            int best_option = -1;
            double min_cut = double.MaxValue;

            for(int i = 0; i < ElementLength.Count; i++)
            {
                // Assign Elements
                if (available[i] && ElementLength[i] >= m.Length) 
                {
                    double current_cut = ElementLength[i] - m.Length;
                    if (current_cut < min_cut)
                    {
                        min_cut = current_cut;
                        best_option = i;
                    }
                    tags.Add($"ID: {i} | Length: {ElementLength[i]} m");
                }
            }

            if (best_option != -1)
            {
                available[best_option] = false;
                total_part_length += m.Length;
                total_stock_used += ElementLength[best_option];

                // Generate Properties
                Point3d start_point = m.PointAt(0);
                Point3d end_point = m.PointAt(1);
                double member_yield = (m.Length / ElementLength[best_option]) * 100;
                string tag = $"From Database Number: {best_option}\nLength of Member in Structure: {m.Length:F2} m\nCut: {member_yield:F2} m\nStart Point: {start_point:F2}\nEnd Point: {end_point:F2}";

                // Construct Bounding Box
                Plane bbox_plane = new Plane(m.PointAt(0.5), m.Direction);
                Interval thickness = new Interval(-0.05, 0.05);
                Interval length = new Interval(-m.Length/2.0, m.Length/2.0);
                Box bbox = new Box(bbox_plane, thickness, thickness, length);

                // Construct Object
                Material assigned_material = new Material(m, bbox, tag);
                assign.Add(assigned_material);
                used.Add(assigned_material.Member);
                
            }

            if (available[best_option] == true || best_option == -1)
            {
                unused.Add(m);
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
    List<Line> used = new List<Line>();
    List<Line> unused = new List<Line>();
    List<string> tags = new List<string>();

    public class Material
    {
        public Line Member;
        public Box Hitbox;
        public string Properties;

        public Material(Line line, Box box, string property)
        {
            Member = line;
            Hitbox = box;
            Properties = property;
        }
    }

    public List<Material> assign = new List<Material>();

    static Sensor mouse_sensor;

    // UI State
    bool show_ui = false;
    int ui_x = 0;
    int ui_y = 0;
    bool is_dragging = false;
    int drag_offset_x = 0;
    int drag_offset_y = 0;

    int ui_width = 220; 
    int ui_height = 130;
    int active_target = 0;

    // Onclick Event
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

            for (int i = 0; i < Owner.assign.Count; i++)
            {
                Interval hit;
                
                if (Rhino.Geometry.Intersect.Intersection.LineBox(laser, Owner.assign[i].Hitbox, 0.01, out hit))
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
            if (assign == null) return;

            if (show_ui && active_target >= 0 && active_target < assign.Count)
            {
                // Highlight Selected Element
                Line selected_element = assign[active_target].Member;
                args.Display.DrawLine(selected_element, Color.FromArgb(255, 200, 0), 4);
            
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
                    body_text = assign[active_target].Properties;
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
