﻿using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Physics
{
    public static class Integrator
    {
        // Integrate the world's motions forward by the given time step.
        public static void Integrate(NativeArray<MotionData> motionDatas, NativeArray<MotionVelocity> motionVelocities, float timeStep)
        {
            for (int i = 0; i < motionDatas.Length; i++)
            {
                ParallelIntegrateMotionsJob.ExecuteImpl(i, motionDatas, motionVelocities, timeStep);
            }
        }

        // Schedule a job to integrate the world's motions forward by the given time step.
        internal static JobHandle ScheduleIntegrateJobs(ref DynamicsWorld world, float timeStep, JobHandle inputDeps, int threadCountHint = 0)
        {
            if (threadCountHint <= 0)
            {
                var job = new IntegrateMotionsJob
                {
                    MotionDatas = world.MotionDatas,
                    MotionVelocities = world.MotionVelocities,
                    TimeStep = timeStep
                };
                return job.Schedule(inputDeps);
            }
            else
            {
                var job = new ParallelIntegrateMotionsJob
                {
                    MotionDatas = world.MotionDatas,
                    MotionVelocities = world.MotionVelocities,
                    TimeStep = timeStep
                };
                return job.Schedule(world.NumMotions, 64, inputDeps);
            }
        }

        [BurstCompile]
        private struct ParallelIntegrateMotionsJob : IJobParallelFor
        {
            public NativeArray<MotionData> MotionDatas;
            public NativeArray<MotionVelocity> MotionVelocities;
            public float TimeStep;

            public void Execute(int i)
            {
                ExecuteImpl(i, MotionDatas, MotionVelocities, TimeStep);
            }

            internal static void ExecuteImpl(int i, NativeArray<MotionData> motionDatas, NativeArray<MotionVelocity> motionVelocities, float timeStep)
            {
                MotionData motionData = motionDatas[i];
                MotionVelocity motionVelocity = motionVelocities[i];

                // Update motion space
                {
                    // center of mass
                    IntegratePosition(ref motionData.WorldFromMotion.pos, motionVelocity.LinearVelocity, timeStep);

                    // orientation
                    IntegrateOrientation(ref motionData.WorldFromMotion.rot, motionVelocity.AngularVelocity, timeStep);
                }

                // Update velocities
                {
                    // damping
                    motionVelocity.LinearVelocity *= math.clamp(1.0f - motionData.LinearDamping * timeStep, 0.0f, 1.0f);
                    motionVelocity.AngularVelocity *= math.clamp(1.0f - motionData.AngularDamping * timeStep, 0.0f, 1.0f);
                }

                // Write back
                motionDatas[i] = motionData;
                motionVelocities[i] = motionVelocity;
            }
        }

        [BurstCompile]
        private struct IntegrateMotionsJob : IJob
        {
            public NativeArray<MotionData> MotionDatas;
            public NativeArray<MotionVelocity> MotionVelocities;
            public float TimeStep;

            public void Execute()
            {
                Integrate(MotionDatas, MotionVelocities, TimeStep);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void IntegratePosition(ref float3 position, float3 linearVelocity, float timestep)
        {
            position += linearVelocity * timestep;
        }

        internal static void IntegrateOrientation(ref quaternion orientation, float3 angularVelocity, float timestep)
        {
            quaternion dq = IntegrateAngularVelocity(angularVelocity, timestep);
            quaternion r = math.mul(orientation, dq);
            orientation = math.normalize(r);
        }

        // Returns a non-normalized quaternion that approximates the change in angle angularVelocity * timestep.
        internal static quaternion IntegrateAngularVelocity(float3 angularVelocity, float timestep)
        {
            float3 halfDeltaTime = new float3(timestep * 0.5f);
            float3 halfDeltaAngle = angularVelocity * halfDeltaTime;
            return new quaternion(new float4(halfDeltaAngle, 1.0f));
        }
    }
}
