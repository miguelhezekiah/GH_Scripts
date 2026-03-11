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
            assign.Clear();
            unused.Clear();
            show_ui = false;
            active_target = -1;

            if (mouse_sensor != null)
            {
                mouse_sensor.Enabled = false;
                mouse_sensor = null;
            }

            return; 
        }    

        if (Run)
        {
            assign.Clear();
            unused.Clear();

            List<Stock> stock = new List<Stock>();
            for(int i = 0; i < ElementLength.Count; i++)
            {
                stock.Add(new Stock(i, ElementLength[i]));
            }

            List<Model> model_element = new List<Model>();
            for(int i = 0; i < Element.Count; i++)
            {
                model_element.Add(new Model(i, Element[i], Element[i].Length));
            }

            stock = stock.OrderBy(w => w.Length).ToList();
            model_element = model_element.OrderByDescending(n => n.Length).ToList();

            foreach (Model m in model_element)
            {
                Stock best_option = null;

                foreach (Stock item in stock)
                {
                    // Assign Elements
                    if (item.IsAvailable && item.Length >= m.Length) 
                    {
                        best_option = item;
                        break;
                    }
                }

                if (best_option != null)
                {
                    best_option.IsAvailable = false;

                    // Generate Properties
                    double cut = best_option.Length - m.Length;
                    Point3d start_point = m.Line.PointAt(0);
                    Point3d end_point = m.Line.PointAt(1);
                    string tag = $"Using database #{best_option.ID}\nLength in model: {m.Length:F2} m\nCut: {cut:F2} m\nStart Point: {start_point:F2}\nEnd Point: {end_point:F2}";

                    // Construct Bounding Box
                    Plane bbox_plane = new Plane(m.Line.PointAt(0.5), m.Line.Direction);
                    Interval thickness = new Interval(-0.25, 0.25);
                    Interval length = new Interval(-m.Length/2.0, m.Length/2.0);
                    Box bbox = new Box(bbox_plane, thickness, thickness, length);

                    // Construct Object
                    Material assigned_material = new Material(m.ID, m.Line, bbox, tag);
                    assign.Add(assigned_material);
                }
                
            }
            
            foreach (Stock item in stock)
            {
                if (item.IsAvailable == true)
                {
                    unused.Add(item.Length);
                }
            }

            // Turn on the sensor
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
        foreach (Material material in assign)
        {
            used.Add(material.Member);
            tags.Add(material.Properties);
        }
        UsedMembers = used;
        UnusedMembers = unused;
        Tags = tags;

        // Redraw
        RhinoDoc.ActiveDoc.Views.Redraw();
    }


    #region Additional
    // <Custom additional code> 
    public class Material
    {
        public int ID;
        public Line Member;
        public Box Hitbox;
        public string Properties;

        public Material(int id, Line line, Box box, string property)
        {
            ID = id;
            Member = line;
            Hitbox = box;
            Properties = property;
        }
    }

    public class Stock
    {
        public int ID;
        public double Length;
        public bool IsAvailable;

        public Stock(int id, double length)
        {
            ID = id;
            Length = length;
            IsAvailable = true;
        }
    }

    public class Model
    {
        public int ID;
        public Line Line;
        public double Length;

        public Model(int id, Line line, double length)
        {
            ID = id;
            Line = line;
            Length = length;
        }
    }

    public List<Material> assign = new List<Material>();
    List<Line> used = new List<Line>();
    List<double> unused = new List<double>();
    List<string> tags = new List<string>();
    static Sensor mouse_sensor;

    // UI State
    bool show_ui = false;
    int ui_x = 0;
    int ui_y = 0;
    bool is_dragging = false;
    int drag_offset_x = 0;
    int drag_offset_y = 0;

    int ui_width = 280; 
    int ui_height = 140;
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
                int header_h = 30;
                
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
                string header_text = $"ITEM #{assign[active_target].ID + 1}";
                Point2d header_pos = new Point2d(x + 10, y + 5);
                args.Display.Draw2dText(header_text, Color.FromArgb(175, 175, 175), header_pos, false, 16);
                
                // Draw Body Text
                Point2d body_pos = new Point2d(x + 10, y + header_h + 10);
                string body_text = "";

                if (tags != null && active_target < tags.Count)
                {
                    body_text = assign[active_target].Properties;
                }

                args.Display.Draw2dText(body_text, Color.FromArgb(175, 175, 175), body_pos, false, 14);
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
