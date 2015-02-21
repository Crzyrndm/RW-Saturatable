using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaturatableRW
{
    public class RWSaturatable : ModuleReactionWheel
    {
        /// <summary>
        /// Storable axis momentum = average axis torque * saturationScale
        /// </summary>
        [KSPField]
        public float saturationScale = 1;

        /// <summary>
        /// Rate at which momentum bleeds off as % of max rate of momentum increase
        /// </summary>
        [KSPField]
        public float bleedRate = 1;

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
        float saturationLimit;

        /// <summary>
        /// Average torque over each axis (quick hack to make calculating the limit easy)
        /// </summary>
        float averageTorque;

        // Storing module torque since this module overrides the base values to take effect
        float maxRollTorque;
        float maxPitchTorque;
        float maxYawTorque;

        /// <summary>
        /// torque available on the roll axis at current saturation
        /// </summary>
        [KSPField(guiActive = true, guiFormat = "F1")]
        float availableRollTorque;

        /// <summary>
        /// torque available on the pitch axis at current saturation
        /// </summary>
        [KSPField(guiActive = true, guiFormat = "F1")]
        float availablePitchTorque;

        /// <summary>
        /// torque available on the yaw axis at current saturation
        /// </summary>
        [KSPField(guiActive = true, guiFormat = "F1")]
        float availableYawTorque;

        /// <summary>
        /// Torque available dependent on % saturation
        /// </summary>
        [KSPField]
        FloatCurve torqueCurve = new FloatCurve();

        public override void OnAwake()
        {
            if (HighLogic.LoadedSceneIsFlight)
                this.part.force_activate();
            base.OnAwake();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            base.OnStart(state);

            maxRollTorque = this.RollTorque;
            maxPitchTorque = this.PitchTorque;
            maxYawTorque = this.YawTorque;

            averageTorque = (this.PitchTorque + this.YawTorque + this.RollTorque) / 3;
            saturationLimit = averageTorque * saturationScale;

            torqueCurve.Add(0, 0);
            torqueCurve.Add(1, 1);
        }

        public override string GetInfo()
        {
            averageTorque = (this.PitchTorque + this.YawTorque + this.RollTorque) / 3;
            saturationLimit = averageTorque * saturationScale;
            string info = string.Format("<b>Pitch Torque:</b> {0:F1} kNm\r\n<b>Yaw Torque:</b> {1:F1} kNm\r\n<b>Roll Torque:</b> {2:F1} kNm\r\n\r\n<b>Capacity:</b> {3:F1} kNms\r\n<b>Bleed Rate:</b> {4:F1}%\r\n\r\n<b><color=#99ff00ff>Requires:</color></b>",
                                        PitchTorque, YawTorque, RollTorque, saturationLimit, bleedRate);

            foreach (ModuleResource res in this.inputResources)
            {
                if (res.rate <= 1)
                    info += string.Format("\r\n - <b>{0}:</b> {1:F1} /min", res.name, res.rate * 60);
                else
                    info += string.Format("\r\n - <b>{0}:</b> {1:F1} /s", res.name, res.rate);
            }
            return info;
            //the string to be shown in the editor module window?
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            
            if (!HighLogic.LoadedSceneIsFlight || this.vessel != FlightGlobals.ActiveVessel || this.State != WheelState.Active)
                return;
            
            // update stored momentum
            updateMomentum();
            // update module torque outputs
            updateTorque();
        }

        private void updateMomentum()
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

            // reduce momentum stored by decay factor
            x_Moment = decayMoment(x_Moment);
            y_Moment = decayMoment(y_Moment);
            z_Moment = decayMoment(z_Moment);
        }

        private void updateTorque()
        {
            // availble{*}Torque = display value
            // this.{*}Torque = actual control value

            // Roll
            availableRollTorque = calcAvailableTorque(this.vessel.transform.up, maxRollTorque);
            this.RollTorque = availableRollTorque;

            // Pitch
            availablePitchTorque = calcAvailableTorque(this.vessel.transform.right, maxPitchTorque);
            this.PitchTorque = availablePitchTorque;

            // Yaw
            availableYawTorque = calcAvailableTorque(this.vessel.transform.forward, maxYawTorque);
            this.YawTorque = availableYawTorque;
        }

        private void inputMoment(Vector3 vesselAxis, float input)
        {
            // increase momentum storage according to axis alignment
            x_Moment += Vector3.Dot(vesselAxis, Planetarium.forward) * input;
            y_Moment += Vector3.Dot(vesselAxis, Planetarium.up) * input;
            z_Moment += Vector3.Dot(vesselAxis, Planetarium.right) * input;
        }

        private float decayMoment(float moment)
        {
            // normalise stored momentum towards zero by a percentage of maximum torque
            if (moment > 0)
                return moment - Mathf.Min(averageTorque * (bleedRate / 100) * TimeWarp.fixedDeltaTime, moment);
            else if (moment < 0)
                return moment + Mathf.Max(averageTorque * (bleedRate / 100) * TimeWarp.fixedDeltaTime, moment);
            else
                return 0;
        }

        private float calcAvailableTorque(Vector3 refAxis, float maxAxisTorque)
        {
            // calculate the torque available from each control axis depending on it's alignment
            Vector3 torqueVec = new Vector3(Vector3.Dot(refAxis, Planetarium.forward) * torqueCurve.Evaluate(pctToSaturation(saturationLimit, x_Moment))
                                            , Vector3.Dot(refAxis, Planetarium.up) * torqueCurve.Evaluate(pctToSaturation(saturationLimit, y_Moment))
                                            , Vector3.Dot(refAxis, Planetarium.right) * torqueCurve.Evaluate(pctToSaturation(saturationLimit, z_Moment)));

            return Mathf.Abs(maxAxisTorque * torqueVec.magnitude);
        }

        /// <summary>
        /// The percentage of momentum before this axis is completely saturated
        /// </summary>
        private float pctToSaturation(float limit, float current)
        {
            return 1 - Mathf.Abs(current) / limit;
        }
    }
}
