using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace SaturatableRW
{
    class RWSaturatable2 : ModuleReactionWheel
    {
        public static KSP.IO.PluginConfiguration config;

        /// <summary>
        /// Storable axis momentum = average axis torque * saturationScale
        /// </summary>
        [KSPField]
        public float saturationScale = 1;

        /// <summary>
        /// Current stored momentum on vessel up axis
        /// </summary>
        [KSPField(isPersistant = true)]
        public float yaw_Moment;

        /// <summary>
        /// Current stored momentum on vessel forward axis
        /// </summary>
        [KSPField(isPersistant = true)]
        public float roll_Moment;

        /// <summary>
        /// Current stored momentum on vessel right axis
        /// </summary>
        [KSPField(isPersistant = true)]
        public float pitch_Moment;

        /// <summary>
        /// Maximum momentum storable on an axis
        /// </summary>
        public float saturationLimit;

        /// <summary>
        /// Average torque over each axis (quick hack to make calculating the limit easy)
        /// </summary>
        float averageTorque;

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
        [KSPField]
        public FloatCurve torqueCurve;

        /// <summary>
        /// Percentage of momentum to decay every second based on % saturation
        /// </summary>
        [KSPField]
        public FloatCurve bleedRate;

        /// <summary>
        /// Whether to decay momentum or not. Decay may not be required if the stabilisation effect is helpful.
        /// </summary>
        [KSPField(isPersistant = true)]
        bool decayMomentum = true;

        [KSPEvent(guiActive = true, active = true, guiName = "Toggle Decay")]
        public void ToggleDecay()
        {
            decayMomentum = !decayMomentum;
        }

        public override void OnAwake()
        {
            base.OnAwake();
            // I need a better way to make this module work at any time
            this.part.force_activate();

            // Float curve initialisation
            if (torqueCurve == null)
                torqueCurve = new FloatCurve();
            if (bleedRate == null)
                bleedRate = new FloatCurve();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            
            if (HighLogic.LoadedSceneIsFlight)
            {
                // remember reference torque values
                maxRollTorque = this.RollTorque;
                maxPitchTorque = this.PitchTorque;
                maxYawTorque = this.YawTorque;
                // average torque is only used for calculating saturation limits (which are the same for all axes)
                
                LoadConfig();
                
                //init_Line();
            }
        }

        public void LoadConfig()
        {
            if (config == null)
                config = KSP.IO.PluginConfiguration.CreateForType<RWSaturatable>();
            config.load();

            if (config.GetValue("LogDump", false))
                StartCoroutine(loggingRoutine());
            if (!config.GetValue("DefaultStateIsActive", true) && vessel.atmDensity > 0.001)
                this.State = ModuleReactionWheel.WheelState.Disabled;
            config["LogDump"] = config.GetValue("LogDump", false);
            config["DefaultStateIsActive"] = config.GetValue("DefaultStateIsActive", true);
            config.save();
        }

        public override string GetInfo()
        {
            averageTorque = (this.PitchTorque + this.YawTorque + this.RollTorque) / 3;
            saturationLimit = averageTorque * saturationScale;

            // calc saturation limit
            averageTorque = (this.PitchTorque + this.YawTorque + this.RollTorque) / 3;
            saturationLimit = averageTorque * saturationScale;

            // Base info
            string info = string.Format("<b>Pitch Torque:</b> {0:F1} kNm\r\n<b>Yaw Torque:</b> {1:F1} kNm\r\n<b>Roll Torque:</b> {2:F1} kNm\r\n\r\n<b>Capacity:</b> {3:F1} kNms",
                                        PitchTorque, YawTorque, RollTorque, saturationLimit);

            // display min/max bleed rate if there is a difference, otherwise just one value
            float min, max;
            bleedRate.FindMinMaxValue(out min, out max);
            if (min == max)
                info += string.Format("\r\n<b>Bleed Rate:</b> {0:F1}%", max * 100);
            else
                info += string.Format("\r\n<b>Bleed Rate:\r\n\tMin:</b> {0:0.#%}\r\n\t<b>Max:</b> {1:0.#%}", min, max);

            // resource consumption
            info += "\r\n\r\n<b><color=#99ff00ff>Requires:</color></b>";
            foreach (ModuleResource res in this.inputResources)
            {
                if (res.rate <= 1)
                    info += string.Format("\r\n - <b>{0}:</b> {1:F1} /min", res.name, res.rate * 60);
                else
                    info += string.Format("\r\n - <b>{0}:</b> {1:F1} /s", res.name, res.rate);
            }
            return info;
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();

            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready)
                return;

            updateMomentum();
            updateTorque();
            resistDirectionChange();
        }

        private void updateMomentum()
        {
            if (this.State == WheelState.Active)
            {
                pitch_Moment += TimeWarp.fixedDeltaTime * this.vessel.ctrlState.pitch * availablePitchTorque;
                roll_Moment += TimeWarp.fixedDeltaTime * this.vessel.ctrlState.roll * availableRollTorque;
                yaw_Moment += TimeWarp.fixedDeltaTime * this.vessel.ctrlState.yaw * availableYawTorque;
            }

            if (decayMomentum)
            {
                pitch_Moment = decayMoment(pitch_Moment, maxPitchTorque);
                roll_Moment = decayMoment(roll_Moment, maxRollTorque);
                yaw_Moment = decayMoment(yaw_Moment, maxYawTorque);
            }
        }

        private void updateTorque()
        {
            availablePitchTorque = torqueCurve.Evaluate(pctSaturation(pitch_Moment, saturationLimit)) * maxPitchTorque;
            this.PitchTorque = availablePitchTorque;

            availableYawTorque = torqueCurve.Evaluate(pctSaturation(yaw_Moment, saturationLimit)) * maxYawTorque;
            this.YawTorque = availableYawTorque;

            availableRollTorque = torqueCurve.Evaluate(pctSaturation(roll_Moment, saturationLimit)) * maxRollTorque;
            this.RollTorque = availableRollTorque;
        }

        private void resistDirectionChange()
        {
            // each moment axis needs to resist separately
            // Vector3 angMomentum = pitch_Moment * vessel.ReferenceTransform.right + yaw_Moment * vessel.ReferenceTransform.up + roll_Moment * vessel.ReferenceTransform.forward;
            // axis of rotation
            Vector3 dir = (vessel.angularVelocity.x * vessel.ReferenceTransform.right + vessel.angularVelocity.y * vessel.ReferenceTransform.up + vessel.angularVelocity.z * vessel.ReferenceTransform.forward).normalized;

            // axis of rotation ignoring any roll
            Vector3 rollStabVec = vessel.angularVelocity.x * vessel.ReferenceTransform.right + vessel.angularVelocity.z * vessel.ReferenceTransform.forward;
            part.Rigidbody.AddTorque(rollStabVec * -roll_Moment);

            Vector3 pitchStabVec = vessel.angularVelocity.y * vessel.ReferenceTransform.up + vessel.angularVelocity.z * vessel.ReferenceTransform.forward;
            part.Rigidbody.AddTorque(pitchStabVec * -pitch_Moment);

            Vector3 yawStabVec = vessel.angularVelocity.x * vessel.ReferenceTransform.right + vessel.angularVelocity.y * vessel.ReferenceTransform.up;
            part.Rigidbody.AddTorque(yawStabVec * -yaw_Moment);

            //if (rollStabVec != Vector3.zero)
            //    line.transform.rotation = Quaternion.LookRotation(rollStabVec);
        }

        /// <summary>
        /// decrease momentum by a percentage of maximum axis torque
        /// </summary>
        private float decayMoment(float moment, float maxTorque)
        {
            float decay = maxTorque * (bleedRate.Evaluate(pctSaturation(moment, saturationLimit))) * TimeWarp.fixedDeltaTime;
            if (moment > decay)
                return moment - decay;
            else if (moment < -decay)
                return moment + decay;
            else
                return 0;
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

        /// <summary>
        /// runs once per second while in the flight scene if logging is active
        /// </summary>
        /// <returns></returns>
        IEnumerator loggingRoutine()
        {
            while (HighLogic.LoadedSceneIsFlight)
            {
                yield return new WaitForSeconds(1);
                Debug.Log(string.Format("Vessel Name: {0}\r\nPart Name: {1}\r\nSaturation Limit: {2}\r\nMomentume X: {3}\r\nMomentum Y: {4}\r\nMomentum Z: {5}\r\nMax Roll Torque: {6}\r\nMax Pitch Torque: {7}"
                    + "\r\nMax Yaw Torque: {8}\r\nAvailable Roll Torque: {9}\r\nAvailable Pitch Torque: {10}\r\nAvailable Yaw Torque: {11}\r\nWheel State: {12}"
                    , this.vessel.vesselName, this.part.partInfo.title, saturationLimit, yaw_Moment, roll_Moment, pitch_Moment, maxRollTorque, maxPitchTorque, maxYawTorque, availableRollTorque, availablePitchTorque, availableYawTorque, this.wheelState));
            }
        }


        LineRenderer line = null;
        void init_Line()
        {
            GameObject obj = new GameObject("Line");

            line = obj.AddComponent<LineRenderer>();
            line.transform.parent = transform;
            line.useWorldSpace = false;
            line.transform.localPosition = Vector3.zero;
            line.transform.localEulerAngles = Vector3.zero;

            line.material = new Material(Shader.Find("Particles/Additive"));
            line.SetColors(Color.red, Color.yellow);
            line.SetWidth(1, 1);
            line.SetVertexCount(2);
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.forward * 5); 
        }
    }
}
