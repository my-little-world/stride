// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Animations;
using Stride.Particles.Sorters;
using Stride.Particles.VertexLayouts;

namespace Stride.Particles.ShapeBuilders
{
    /// <summary>
    /// Shape builder which builds each particle as a camera-facing hexagon
    /// </summary>
    [DataContract("ShapeBuilderHexagon")]
    [Display("Hexagon")]
    public class ShapeBuilderHexagon : ShapeBuilderCommon
    {
        /// <inheritdoc />
        public override int QuadsPerParticle { get; protected set; } = 2;

        /// <summary>
        /// Additive animation for the particle rotation. If present, particle's own rotation will be added to the sampled curve value
        /// </summary>
        /// <userdoc>
        /// Additive animation for the particle rotation. If present, particle's own rotation will be added to the sampled curve value
        /// </userdoc>
        [DataMember(300)]
        [Display("Additive Rotation Animation")]
        public ComputeCurveSampler<float> SamplerRotation { get; set; }

        /// <inheritdoc />
        public override void PreUpdate()
        {
            base.PreUpdate();

            SamplerRotation?.UpdateChanges();
        }

        /// <inheritdoc />
        public override unsafe int BuildVertexBuffer(ref ParticleBufferState bufferState, Vector3 invViewX, Vector3 invViewY,
            ref Vector3 spaceTranslation, ref Quaternion spaceRotation, float spaceScale, ref ParticleList sorter, ref Matrix viewProj)
        {
            // Update the curve samplers if required
            base.BuildVertexBuffer(ref bufferState, invViewX, invViewY, ref spaceTranslation, ref spaceRotation, spaceScale, ref sorter, ref viewProj);

            // Get all required particle fields
            var positionField = sorter.GetField(ParticleFields.Position);
            if (!positionField.IsValid())
                return 0;
            var sizeField = sorter.GetField(ParticleFields.Size);
            var lifeField = sorter.GetField(ParticleFields.Life);
            var angleField = sorter.GetField(ParticleFields.Angle);
            var hasAngle = angleField.IsValid() || (SamplerRotation != null);


            // Check if the draw space is identity - in this case we don't need to transform the position, scale and rotation vectors
            var trsIdentity = (spaceScale == 1f);
            trsIdentity = trsIdentity && (spaceTranslation.Equals(Vector3.Zero));
            trsIdentity = trsIdentity && (spaceRotation.Equals(Quaternion.Identity));


            var renderedParticles = 0;

            var posAttribute = bufferState.GetAccessor(VertexAttributes.Position);
            var texAttribute = bufferState.GetAccessor(bufferState.DefaultTexCoords);

            foreach (var particle in sorter)
            {
                var centralPos = GetParticlePosition(particle, positionField, lifeField);

                var particleSize = GetParticleSize(particle, sizeField, lifeField);

                if (!trsIdentity)
                {
                    spaceRotation.Rotate(ref centralPos);
                    centralPos = centralPos * spaceScale + spaceTranslation;
                    particleSize *= spaceScale;
                }

                // Use half size to make a Size = 1 result in a Billboard of 1m x 1m
                var unitX = invViewX * (particleSize * 0.5f); 
                var unitY = invViewY * (particleSize * 0.5f); 

                // Particle rotation. Positive value means clockwise rotation.
                if (hasAngle)
                {
                    var rotationAngle = GetParticleRotation(particle, angleField, lifeField);
                    var cosA = MathF.Cos(rotationAngle);
                    var sinA = MathF.Sin(rotationAngle);
                    var tempX = unitX * cosA - unitY * sinA;
                    unitY = unitY * cosA + unitX * sinA;
                    unitX = tempX;
                }

                // vertex.Size = particleSize;

                const float Sqrt3Half = 0.86602540378f;
                unitY *= Sqrt3Half;
                var halfX = unitX * 0.5f;

                var particlePos = centralPos - halfX + unitY;
                var uvCoord = new Vector2(0.25f, 0.5f - Sqrt3Half * 0.5f);


                // Upper half

                // 0f 0f
                bufferState.SetAttribute(posAttribute, (IntPtr)(&particlePos));
                bufferState.SetAttribute(texAttribute, (IntPtr)(&uvCoord));
                bufferState.NextVertex();


                // 1f 0f
                particlePos += unitX;
                uvCoord.X = 0.75f;
                bufferState.SetAttribute(posAttribute, (IntPtr)(&particlePos));
                bufferState.SetAttribute(texAttribute, (IntPtr)(&uvCoord));
                bufferState.NextVertex();


                // 1f 1f
                particlePos += halfX;
                particlePos -= unitY;
                uvCoord.X = 1;
                uvCoord.Y = 0.5f;
                bufferState.SetAttribute(posAttribute, (IntPtr)(&particlePos));
                bufferState.SetAttribute(texAttribute, (IntPtr)(&uvCoord));
                bufferState.NextVertex();


                // 0f 1f
                particlePos -= unitX * 2;
                uvCoord.X = 0;
                bufferState.SetAttribute(posAttribute, (IntPtr)(&particlePos));
                bufferState.SetAttribute(texAttribute, (IntPtr)(&uvCoord));
                bufferState.NextVertex();

                // Upper half

                // 0f 0f
                bufferState.SetAttribute(posAttribute, (IntPtr)(&particlePos));
                bufferState.SetAttribute(texAttribute, (IntPtr)(&uvCoord));
                bufferState.NextVertex();


                // 1f 0f
                particlePos += unitX * 2;
                uvCoord.X = 1;
                bufferState.SetAttribute(posAttribute, (IntPtr)(&particlePos));
                bufferState.SetAttribute(texAttribute, (IntPtr)(&uvCoord));
                bufferState.NextVertex();


                // 1f 1f
                particlePos -= halfX;
                particlePos -= unitY;
                uvCoord.X = 0.75f;
                uvCoord.Y = 1;
                bufferState.SetAttribute(posAttribute, (IntPtr)(&particlePos));
                bufferState.SetAttribute(texAttribute, (IntPtr)(&uvCoord));
                bufferState.NextVertex();


                // 0f 1f
                particlePos -= unitX;
                uvCoord.X = 0.25f;
                bufferState.SetAttribute(posAttribute, (IntPtr)(&particlePos));
                bufferState.SetAttribute(texAttribute, (IntPtr)(&uvCoord));
                bufferState.NextVertex();

                renderedParticles++;
            }

            var vtxPerShape = 4 * QuadsPerParticle;
            return renderedParticles * vtxPerShape;
        }

        /// <summary>
        /// Gets the combined rotation for the particle, adding its field value (if any) to its sampled value from the curve
        /// </summary>
        /// <param name="particle">Target particle</param>
        /// <param name="rotationField">Rotation field accessor</param>
        /// <param name="lifeField">Normalized particle life for sampling</param>
        /// <returns>Screen space rotation in radians, positive is clockwise</returns>
        protected unsafe float GetParticleRotation(Particle particle, ParticleFieldAccessor<float> rotationField, ParticleFieldAccessor<float> lifeField)
        {
            var particleRotation = rotationField.IsValid() ? particle.Get(rotationField) : 1f;

            if (SamplerRotation == null)
                return particleRotation;

            var life = 1f - (*((float*)particle[lifeField]));   // The Life field contains remaining life, so for sampling we take (1 - life)

            return particleRotation + MathUtil.DegreesToRadians(SamplerRotation.Evaluate(life));
        }

    }
}
