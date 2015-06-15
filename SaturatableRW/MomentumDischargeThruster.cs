using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaturatableRW
{
    public class MomentumDischargeThruster : ModuleRCS
    {
        public override string GetInfo()
        {
            string baseInfo = base.GetInfo();
            int index = baseInfo.IndexOf("<color=#99ff00ff><b>Requires:</b></color>");
            string resourceRates = baseInfo.Substring(index);
            return string.Format("Thruster used to remove accumulated momentum from a RW\r\n<b>Discharge Rate:</b> {0}% / s\r\n\r\n{1}", (thrusterPower * 100).ToString("0.0"), resourceRates);
        }

        public override void OnAwake()
        {
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
            {
                foreach (BaseField f in Fields) // hide "RCS ISP" field
                {
                    f.guiActive = false;
                    f.guiActiveEditor = false;
                }
                foreach (BaseEvent e in Events) // hide "disable RCS port" button
                {
                    e.guiActive = false;
                    e.guiActiveEditor = false;
                    e.guiActiveUnfocused = false;
                }
            }
            base.OnAwake();
        }
    }
}
