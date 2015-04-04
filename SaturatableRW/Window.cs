using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaturatableRW
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class Window : MonoBehaviour
    {
        static Window instance;
        public static Window Instance
        {
            get 
            {
                return instance;
            }
        }


        // UI Stuff
        public List<RWSaturatable> wheelsToDraw = new List<RWSaturatable>();
        Rect windowRect = new Rect();
        public bool showWindow = false;

        void Start()
        {
            InitWindow();
        }

        private void InitWindow()
        {
            instance = this;
            
            RWSaturatable.LoadConfig();
            loadConfig();

            RenderingManager.AddToPostDrawQueue(5, draw);
            Debug.Log("Draw call added");
        }

        void loadConfig()
        {
            windowRect = RWSaturatable.config.GetValue("windowRect", new Rect(500, 500, 300, 0));
            RWSaturatable.config["windowRect"] = windowRect;
        }

        void OnDestroy()
        {
            RWSaturatable.config["windowRect"] = windowRect;
            RWSaturatable.config.save();
        }

        void draw()
        {
            GUI.skin = HighLogic.Skin;

            if (showWindow)
                windowRect = GUILayout.Window(573638, windowRect, drawWindow, "Semi-Saturatable Reaction Wheels", GUILayout.Height(0));
        }

        void drawWindow(int id)
        {
            foreach (RWSaturatable rw in wheelsToDraw)
                drawWheel(rw);

            GUI.DragWindow();
        }

        void drawWheel(RWSaturatable rw)
        {
            Color backgroundColour = GUI.backgroundColor;
            if (rw.vessel == FlightGlobals.ActiveVessel)
                GUI.backgroundColor = XKCDColors.Green;
            rw.drawWheel = GUILayout.Toggle(rw.drawWheel, rw.part.partInfo.title, GUI.skin.button);
            GUI.backgroundColor = backgroundColour;

            if (!rw.drawWheel)
                return;
            bool state = GUILayout.Toggle(rw.State == ModuleReactionWheel.WheelState.Active ? true : false, "Toggle Torque");
            rw.State = state ? ModuleReactionWheel.WheelState.Active : ModuleReactionWheel.WheelState.Disabled;
            
            GUILayout.Label("\t\t<b>Axis</b>\t\t<b>Available</b>\t\t<b>Max</b>");
            GUILayout.Label(string.Format("\t\t{0}\t\t{1:0.0}kN\t\t\t{2:0.0}kN", "Pitch", rw.availablePitchTorque, rw.maxPitchTorque));
            GUILayout.Label(string.Format("\t\t{0}\t\t{1:0.0}kN\t\t\t{2:0.0}kN", "Yaw", rw.availableYawTorque, rw.maxYawTorque));
            GUILayout.Label(string.Format("\t\t{0}\t\t{1:0.0}kN\t\t\t{2:0.0}kN", "Roll", rw.availableRollTorque, rw.maxRollTorque));
        }
    }
}
