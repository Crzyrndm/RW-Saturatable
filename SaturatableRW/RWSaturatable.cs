using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace SaturatableRW
{
    public class RWSaturatable : PartModule
    {
        /*//////////////////////////////////////////////////////////////////////////////
         * This is not and is never intended to be a realistic representation of how reaction wheels work. That would involve simulating
         * effects such as gyroscopic stabilisation and precession that are not dependent only on the internal state of the part and current
         * command inputs, but the rate of rotation of the vessel and would require applying forces without using the input system
         * 
         * Instead, a reaction wheel is simulated as an arbitrary object in a fixed orientation in space. Momentum is
         * attributed to and decayed from these objects based on vessel alignment with their arbitrary axes. This system allows for
         * a reasonable approximation of RW effects on control input but there are very noticeable inconsistencies with a realistic
         * system.
        /*///////////////////////////////////////////////////////////////////////////////

        internal ModuleReactionWheel wheelRef;

        /// <summary>
        /// Storable axis momentum = average axis torque * saturationScale
        /// </summary>
        [KSPField]
        public float saturationScale = 1;

        /// <summary>
        /// Current stored momentum on X axis
        /// </summary>
        [KSPField(isPersistant = true)]
        public float x_Moment;

        /// <summary>
        /// Current stored momentum on Y axis
        /// </summary>
        [KSPField(isPersistant = true)]
        public float y_Moment;

        /// <summary>
        /// Current stored momentum on Z axis
        /// </summary>
        [KSPField(isPersistant = true)]
        public float z_Moment;

        /// <summary>
        /// Maximum momentum storable on an axis
        /// </summary>
        public float saturationLimit;
        
        // Storing module torque for reference since this module overrides the base values to take effect
        public float maxRollTorque;
        public float maxPitchTorque;
        public float maxYawTorque;

        /// <summary>
        /// torque available on the roll axis at current saturation
        /// </summary>
        [KSPField(guiActive = true, guiFormat = "F1")]
        public float availableRollTorque;

        /// <summary>
        /// torque available on the pitch axis at current saturation
        /// </summary>
        [KSPField(guiActive = true, guiFormat = "F1")]
        public float availablePitchTorque;

        /// <summary>
        /// torque available on the yaw axis at current saturation
        /// </summary>
        [KSPField(guiActive = true, guiFormat = "F1")]
        public float availableYawTorque;

        /// <summary>
        /// Torque available dependent on % saturation
        /// </summary>
        public FloatCurve torqueCurve;

        /// <summary>
        /// Percentage of momentum to decay every second based on % saturation
        /// </summary>
        public FloatCurve bleedRate;

        /// <summary>
        /// Globally scale down output torque
        /// </summary>
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Torque Throttle"), UI_FloatRange(minValue = 0, maxValue = 1, stepIncrement = 0.02f, scene = UI_Scene.Editor)]
        public float torqueThrottle = 1;

        /// <summary>
        /// When true, wheel will dump momentum at a fixed rate in exchange for a certain amount of a resource (eg. monopropellant)
        /// Toggle through the window (and sets false when it runs out of resources or stored momentum)
        /// </summary>
        public bool bConsumeResource = false;

        public List<ResourceConsumer> dischargeResources;
        public MomentumDischargeThruster dummyRCS;
        public class ResourceConsumer
        {
            public int ID { get; set; }
            public double Rate { get; set; }
            public ResourceFlowMode FlowMode { get; set; }
            public ResourceConsumer(int id, double rate, ResourceFlowMode flowMode)
            {
                ID = id;
                Rate = rate;
                FlowMode = flowMode;
            }
        }
        /// <summary>
        /// if false, resource consumption is disallowed
        /// </summary>
        public bool canForceDischarge = false;

        /// <summary>
        /// If true, resource discharge creates torque equivalent to the momentum discharged
        /// </summary>
        public bool dischargeTorque = false;

        /// <summary>
        /// The momentum to recover per second of discharge
        /// </summary>
        public float dischargeRate;

        public bool drawWheel = false;

        public static KSP.IO.PluginConfiguration config;

        [KSPEvent(guiActive = true, active = true, guiName = "Toggle RW Window")]
        public void ToggleWindow()
        {
            Window.Instance.showWindow = !Window.Instance.showWindow;
        }

        public void OnDestroy()
        {
            if (!HighLogic.LoadedSceneIsFlight || Window.Instance == null || Window.Instance.Vessels == null)
                return;
            try
            {
                if (Window.Instance.Vessels.ContainsKey(vessel.vesselName))
                    Window.Instance.Vessels.Remove(vessel.vesselName);
            }
            catch //(Exception ex)
            {
                //Debug.Log(ex.StackTrace);
            }
        }

        public void Start()
        {
            foreach (ConfigNode node in part.partInfo.partConfig.GetNodes("MODULE"))
            {
                if (node.GetValue("name") == "RWSaturatable")
                {
                    torqueCurve.Load(node.GetNode("torqueCurve"));
                    bleedRate.Load(node.GetNode("bleedRate"));
                    break;
                }
            }
            
            wheelRef = part.Modules.GetModule<ModuleReactionWheel>();

            // Float curve initialisation



            maxRollTorque = wheelRef.RollTorque;
            maxPitchTorque = wheelRef.PitchTorque;
            maxYawTorque = wheelRef.YawTorque;

            saturationLimit = (maxPitchTorque + maxYawTorque + maxRollTorque) * saturationScale / 3;
            if (HighLogic.LoadedSceneIsFlight)
            {
                // remember reference torque values

                LoadConfig();
                                
                StartCoroutine(registerWheel());
            }
        }

        IEnumerator registerWheel()
        {
            yield return null;

            dischargeResources = new List<ResourceConsumer>();
            dummyRCS = part.Modules.GetModule<MomentumDischargeThruster>();
            if (dummyRCS != null)
            {
                dischargeRate = dummyRCS.thrusterPower;
                double ISP = dummyRCS.atmosphereCurve.Evaluate(0);
                double totalPropellantMassRatio = dummyRCS.propellants.Sum(r => r.ratio * PartResourceLibrary.Instance.resourceDefinitions[r.id].density);
                double totalMassRate = dummyRCS.thrusterPower * saturationLimit / (ISP * dummyRCS.G);
                foreach (Propellant p in dummyRCS.propellants)
                {
                    PartResourceDefinition res = PartResourceLibrary.Instance.resourceDefinitions[p.id];
                    double propellantRate = p.ratio * totalMassRate / totalPropellantMassRatio;
                    dischargeResources.Add(new ResourceConsumer(res.id, propellantRate, res.resourceFlowMode));
                }
                if (dischargeResources.Any(rc => rc.Rate > 0))
                    canForceDischarge = true;
            }

            for (int i = 0; i < 10; i++)
                yield return null;
            
            if (!Window.Instance.Vessels.ContainsKey(vessel.vesselName))
                Window.Instance.Vessels.Add(vessel.vesselName, new VesselInfo(vessel, wheelRef.State == ModuleReactionWheel.WheelState.Active));

            Window.Instance.Vessels[vessel.vesselName].wheels.Add(this);
        }

        public void LoadConfig()
        {
            if (config == null)
                config = KSP.IO.PluginConfiguration.CreateForType<RWSaturatable>();
            config.load();

            if (!config.GetValue("DefaultStateIsActive", true) && vessel.atmDensity > 0.001)
                wheelRef.State = ModuleReactionWheel.WheelState.Disabled;
            if (!config.GetValue("DisplayCurrentTorque", false))
            {
                this.Fields["availablePitchTorque"].guiActive = false;
                this.Fields["availableRollTorque"].guiActive = false;
                this.Fields["availableYawTorque"].guiActive = false;
            }
            if (config.GetValue("dischargeTorque", false))
                dischargeTorque = true;

            // save the file so it can be activated by anyone
            config["DefaultStateIsActive"] = config.GetValue("DefaultStateIsActive", true);
            config["DisplayCurrentTorque"] = config.GetValue("DisplayCurrentTorque", false);
            config["dischargeTorque"] = config.GetValue("dischargeTorque", false);
            config.save();
        }

        private string info = string.Empty;
        public override string GetInfo()
        {
            if (HighLogic.LoadedSceneIsEditor && string.IsNullOrEmpty(info))
            {
                saturationLimit = (wheelRef.PitchTorque + wheelRef.YawTorque + wheelRef.RollTorque) * saturationScale / 3;

                // Base info
                info = string.Format("<b>Pitch Torque:</b> {0:F1} kNm\r\n<b>Yaw Torque:</b> {1:F1} kNm\r\n<b>Roll Torque:</b> {2:F1} kNm\r\n\r\n<b>Capacity:</b> {3:F1} kNms",
                                            wheelRef.PitchTorque, wheelRef.YawTorque, wheelRef.RollTorque, saturationLimit);

                // display min/max bleed rate if there is a difference, otherwise just one value
                float min, max;
                bleedRate.FindMinMaxValue(out min, out max);
                if (min == max)
                    info += string.Format("\r\n<b>Bleed Rate:</b> {0:F1}%", max * 100);
                else
                    info += string.Format("\r\n<b>Bleed Rate:\r\n\tMin:</b> {0:0.#%}\r\n\t<b>Max:</b> {1:0.#%}", min, max);

                //// resource consumption
                //info += "\r\n\r\n<b><color=#99ff00ff>Requires:</color></b>";
                //foreach (ModuleResource res in wheelRef.GetConsumedResources())
                //{
                //    if (res.rate <= 1)
                //        info += string.Format("\r\n - <b>{0}:</b> {1:F1} /min", res.name, res.rate * 60);
                //    else
                //        info += string.Format("\r\n - <b>{0}:</b> {1:F1} /s", res.name, res.rate);
                //}
            }
            return info;
        }

        public void FixedUpdate()
        {
            if (!(HighLogic.LoadedSceneIsFlight && FlightGlobals.ready))
                return;
            //useResourcesToRecover();

            // update stored momentum
            updateMomentum();

            // update module torque outputs
            updateTorque();
        }

        //Vector3 lastRemovedMoment;
        //private void useResourcesToRecover()
        //{
        //    if (!bConsumeResource || !canForceDischarge)
        //        return;

        //    float momentumToRemove = TimeWarp.fixedDeltaTime * dischargeRate * saturationLimit;
        //    float x_momentToRemove = Mathf.Clamp(x_Moment, -momentumToRemove, momentumToRemove);
        //    float y_momentToRemove = Mathf.Clamp(y_Moment, -momentumToRemove, momentumToRemove);
        //    float z_momentToRemove = Mathf.Clamp(z_Moment, -momentumToRemove, momentumToRemove);
        //    double resourcePctToRequest = (Math.Abs(x_momentToRemove) + Math.Abs(y_momentToRemove) + Math.Abs(z_momentToRemove)) / (3 * momentumToRemove); // reduce the resource consumption if less is removed
        //    if (resourcePctToRequest < 0.01)
        //    {
        //        bConsumeResource = false;
        //        return;
        //    }

        //    // I'm looping through like this because I need to know the minimum pct available across all the resources to be consumed otherwise the last one might run low and cause uneven draw
        //    // if only one resource specified lets just not do this extra resource check...
        //    double pctRequestable = 1;
        //    if (dischargeResources.Count > 1)
        //    {
        //        foreach (ResourceConsumer rc in dischargeResources)
        //        {
        //            double total = getConnectedResources(rc).Sum(r => r.amount);
        //            double requestedAmount = rc.Rate * resourcePctToRequest * TimeWarp.fixedDeltaTime;
        //            pctRequestable = Math.Min(total / requestedAmount, pctRequestable);
        //        }
        //    }
        //    if (pctRequestable < 0.01)
        //    {
        //        bConsumeResource = false;
        //        return;
        //    }

        //    float momentFrac = (float)pctRequestable;
        //    foreach (ResourceConsumer rc in dischargeResources)
        //    {
        //        double amount = rc.Rate * resourcePctToRequest * pctRequestable * TimeWarp.fixedDeltaTime;
        //        momentFrac = (float)Math.Min(momentFrac, part.RequestResource(rc.ID, amount) / amount);
        //    }
        //    x_Moment -= x_momentToRemove * momentFrac;
        //    y_Moment -= y_momentToRemove * momentFrac;
        //    z_Moment -= z_momentToRemove * momentFrac;

        //    lastRemovedMoment = new Vector3(x_momentToRemove * momentFrac, y_momentToRemove * momentFrac, z_momentToRemove * momentFrac);
        //    if (dischargeTorque)
        //        part.AddTorque(vessel.ReferenceTransform.rotation * -lastRemovedMoment);
        //}

        //public List<PartResource> getConnectedResources(ResourceConsumer rc)
        //{
        //    List<PartResource> connectedResources = new List<PartResource>();
        //    part.GetConnectedResources(rc.ID, rc.FlowMode, connectedResources);
        //    return connectedResources;
        //}

        private void updateMomentum()
        {
            if (wheelRef.State == ModuleReactionWheel.WheelState.Active && !bConsumeResource)
            {
                // input torque scale. Available torque gives exponential decay and will always have some torque available (should asymptotically approach bleed rate)
                float rollInput = TimeWarp.fixedDeltaTime * this.vessel.ctrlState.roll * availableRollTorque;
                float pitchInput = TimeWarp.fixedDeltaTime * this.vessel.ctrlState.pitch * availablePitchTorque;
                float yawInput = TimeWarp.fixedDeltaTime * this.vessel.ctrlState.yaw * availableYawTorque;
                // increase momentum stored according to relevant inputs
                // transform is based on the vessel not the part. 0 Pitch torque gives no pitch response for any part orientation
                // roll == up vector
                // yaw == forward vector
                // pitch == right vector
                inputMoment(this.vessel.transform.up, rollInput);
                inputMoment(this.vessel.transform.right, pitchInput);
                inputMoment(this.vessel.transform.forward, yawInput);
            }

            // reduce momentum stored by decay factor
            x_Moment = decayMoment(x_Moment, Planetarium.forward);
            y_Moment = decayMoment(y_Moment, Planetarium.up);
            z_Moment = decayMoment(z_Moment, Planetarium.right);
        }

        private void updateTorque()
        {
            // available{*}Torque = display value
            // this.{*}Torque = actual control value

            // Roll
            availableRollTorque = Math.Abs(calcAvailableTorque(this.vessel.transform.up, maxRollTorque)) * torqueThrottle;
            wheelRef.RollTorque = availableRollTorque;

            // Pitch
            availablePitchTorque = Math.Abs(calcAvailableTorque(this.vessel.transform.right, maxPitchTorque)) * torqueThrottle;
            wheelRef.PitchTorque = bConsumeResource ? 0 : availablePitchTorque;

            // Yaw
            availableYawTorque = Math.Abs(calcAvailableTorque(this.vessel.transform.forward, maxYawTorque)) * torqueThrottle;
            wheelRef.YawTorque = bConsumeResource ? 0 : availableYawTorque;
        }

        /// <summary>
        /// increase momentum storage according to axis alignment
        /// </summary>
        private void inputMoment(Vector3 vesselAxis, float input)
        {
            Vector3 scaledAxis = vesselAxis * input;

            x_Moment += Vector3.Dot(scaledAxis, Planetarium.forward);
            y_Moment += Vector3.Dot(scaledAxis, Planetarium.up);
            z_Moment += Vector3.Dot(scaledAxis, Planetarium.right);
        }

        /// <summary>
        /// decrease momentum stored by a set percentage of the maximum torque that could be exerted on this axis
        /// </summary>
        private float decayMoment(float moment, Vector3 refAxis)
        {
            float torqueMag = new Vector3(Vector3.Dot(this.vessel.transform.right, refAxis) * maxPitchTorque
                                        , Vector3.Dot(this.vessel.transform.forward, refAxis) * maxYawTorque
                                        , Vector3.Dot(this.vessel.transform.up, refAxis) * maxRollTorque).magnitude;

            float decay = torqueMag * (bleedRate.Evaluate(pctSaturation(moment, saturationLimit))) * TimeWarp.fixedDeltaTime;
            if (moment > decay)
                return moment - decay;
            else if (moment < -decay)
                return moment + decay;
            else
                return 0;
        }

        /// <summary>
        /// The available torque for a given vessel axis and torque based on the momentum stored in world space
        /// </summary>
        private float calcAvailableTorque(Vector3 refAxis, float maxAxisTorque)
        {
            Vector3 torqueVec = new Vector3(Vector3.Dot(refAxis, Planetarium.forward), Vector3.Dot(refAxis, Planetarium.up), Vector3.Dot(refAxis, Planetarium.right));
            
            // Smallest ratio is the scaling factor so set them huge as a default
            float ratiox = 1000000, ratioy = 1000000, ratioz = 1000000;
            if (torqueVec.x != 0)
                ratiox = Mathf.Abs(torqueCurve.Evaluate(pctSaturation(x_Moment, saturationLimit)) / torqueVec.x);
            if (torqueVec.y != 0)
                ratioy = Mathf.Abs(torqueCurve.Evaluate(pctSaturation(y_Moment, saturationLimit)) / torqueVec.y);
            if (torqueVec.z != 0)
                ratioz = Mathf.Abs(torqueCurve.Evaluate(pctSaturation(z_Moment, saturationLimit)) / torqueVec.z);

            return torqueVec.magnitude * Mathf.Min(ratiox, ratioy, ratioz, 1) * maxAxisTorque;
        }

        /// <summary>
        /// The percentage of momentum before this axis is completely saturated
        /// </summary>
        private float pctSaturation(float current, float limit)
        {
            if (limit != 0)
                return Mathf.Abs(current) / limit;
            else
                return 0;
        }
    }
}
