using System;
using System.Collections.Generic;
using System.Linq;

namespace SaturatableRW
{
    // global vars for a vessel and a list of all RW's
    public class VesselInfo
    {
        public Vessel vessel { get; set; }
        public bool forcedActive { get; set; }
        public List<RWSaturatable> wheels { get; set; }
        public bool displayVes { get; set; }

        public VesselInfo(Vessel ves, bool active)
        {
            vessel = ves;
            forcedActive = active;
            wheels = new List<RWSaturatable>();
        }
    }
}
