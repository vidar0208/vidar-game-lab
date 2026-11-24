// URP port of Hidden/Clouds from SebLague/Clouds (MIT, Copyright (c) 2019 Sebastian Lague), run by URP's
// Full Screen Pass Renderer Feature instead of OnRenderImage + Graphics.Blit.
// Same algorithm: ray-box intersection, fixed-size steps along the view ray, density from a 3D Worley/FBM texture
// eroded by a detail texture, light march toward the sun with Beer's law, dual Henyey-Greenstein phase.
// Changes: URP includes; scene depth from URP's depth texture; sun direction/colour and time come from C#;
// the view step size (a const 11 in the source) and a density-only switch are parameters for the comparison;
// the unused weather map and noise debug viewer are left out.
Shader "GameTech/Clouds"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Name "Clouds"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE3D(NoiseTex); SAMPLER(samplerNoiseTex);
            TEXTURE3D(DetailNoiseTex); SAMPLER(samplerDetailNoiseTex);
            TEXTURE2D(BlueNoise); SAMPLER(samplerBlueNoise);

            // Shape settings
            float densityMultiplier, densityOffset, scale, detailNoiseScale, detailNoiseWeight;
            float3 detailWeights; float4 shapeNoiseWeights; float4 phaseParams;
            // March settings
            int numStepsLight; float rayOffsetStrength; float stepSize; int lightingOn;
            float3 boundsMin, boundsMax, shapeOffset, detailOffset;
            // Light settings
            float lightAbsorptionTowardSun, lightAbsorptionThroughCloud, darknessThreshold;
            float3 sunDir; float4 sunColor;
            // Animation settings
            float cloudTime, timeScale, baseSpeed, detailSpeed;

            float remap(float v, float minOld, float maxOld, float minNew, float maxNew) {
                return minNew + (v-minOld) * (maxNew - minNew) / (maxOld-minOld);
            }

            float2 squareUV(float2 uv) {
                float scale = 1000;
                return float2(uv.x * _ScreenParams.x / scale, uv.y * _ScreenParams.y / scale);
            }

            // Returns (dstToBox, dstInsideBox). If ray misses box, dstInsideBox will be zero
            float2 rayBoxDst(float3 boundsMin, float3 boundsMax, float3 rayOrigin, float3 invRaydir) {
                float3 t0 = (boundsMin - rayOrigin) * invRaydir;
                float3 t1 = (boundsMax - rayOrigin) * invRaydir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                float dstA = max(max(tmin.x, tmin.y), tmin.z);
                float dstB = min(tmax.x, min(tmax.y, tmax.z));
                float dstToBox = max(0, dstA);
                float dstInsideBox = max(0, dstB - dstToBox);
                return float2(dstToBox, dstInsideBox);
            }

            // Henyey-Greenstein
            float hg(float a, float g) {
                float g2 = g*g;
                return (1-g2) / (4*3.1415*pow(1+g2-2*g*(a), 1.5));
            }

            float phase(float a) {
                float blend = .5;
                float hgBlend = hg(a,phaseParams.x) * (1-blend) + hg(a,-phaseParams.y) * blend;
                return phaseParams.z + hgBlend*phaseParams.w;
            }

            float sampleDensity(float3 rayPos) {
                const float baseScale = 1/1000.0;
                const float offsetSpeed = 1/100.0;
                float time = cloudTime * timeScale;
                float3 size = boundsMax - boundsMin;
                float3 uvw = (size * .5 + rayPos) * baseScale * scale;
                float3 shapeSamplePos = uvw + shapeOffset * offsetSpeed + float3(time,time*0.1,time*0.2) * baseSpeed;

                // falloff along x/z edges of the cloud container
                const float containerEdgeFadeDst = 50;
                float dstFromEdgeX = min(containerEdgeFadeDst, min(rayPos.x - boundsMin.x, boundsMax.x - rayPos.x));
                float dstFromEdgeZ = min(containerEdgeFadeDst, min(rayPos.z - boundsMin.z, boundsMax.z - rayPos.z));
                float edgeWeight = min(dstFromEdgeZ,dstFromEdgeX)/containerEdgeFadeDst;

                // height gradient
                float gMin = .2;
                float gMax = .7;
                float heightPercent = (rayPos.y - boundsMin.y) / size.y;
                float heightGradient = saturate(remap(heightPercent, 0.0, gMin, 0, 1)) * saturate(remap(heightPercent, 1, gMax, 0, 1));
                heightGradient *= edgeWeight;

                // base shape density
                float4 shapeNoise = SAMPLE_TEXTURE3D_LOD(NoiseTex, samplerNoiseTex, shapeSamplePos, 0);
                float4 normalizedShapeWeights = shapeNoiseWeights / dot(shapeNoiseWeights, 1);
                float shapeFBM = dot(shapeNoise, normalizedShapeWeights) * heightGradient;
                float baseShapeDensity = shapeFBM + densityOffset * .1;

                if (baseShapeDensity > 0) {
                    float3 detailSamplePos = uvw*detailNoiseScale + detailOffset * offsetSpeed + float3(time*.4,-time,time*0.1)*detailSpeed;
                    float4 detailNoise = SAMPLE_TEXTURE3D_LOD(DetailNoiseTex, samplerDetailNoiseTex, detailSamplePos, 0);
                    float3 normalizedDetailWeights = detailWeights / dot(detailWeights, 1);
                    float detailFBM = dot(detailNoise.xyz, normalizedDetailWeights);
                    // erode edges more than the centre
                    float oneMinusShape = 1 - shapeFBM;
                    float detailErodeWeight = oneMinusShape * oneMinusShape * oneMinusShape;
                    float cloudDensity = baseShapeDensity - (1-detailFBM) * detailErodeWeight * detailNoiseWeight;
                    return cloudDensity * densityMultiplier * 0.1;
                }
                return 0;
            }

            // proportion of light that reaches the given point from the light source
            float lightmarch(float3 position) {
                float3 dirToLight = sunDir;
                float dstInsideBox = rayBoxDst(boundsMin, boundsMax, position, 1/dirToLight).y;
                float stepSizeL = dstInsideBox/numStepsLight;
                float totalDensity = 0;
                for (int step = 0; step < numStepsLight; step ++) {
                    position += dirToLight * stepSizeL;
                    totalDensity += max(0, sampleDensity(position) * stepSizeL);
                }
                float transmittance = exp(-totalDensity * lightAbsorptionTowardSun);
                return darknessThreshold + transmittance * (1-darknessThreshold);
            }

            float4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float3 backgroundCol = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // ray from the camera through this pixel; scene depth as a distance along the ray
                float rawDepth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 rayPos = _WorldSpaceCameraPos;
                float3 toScene = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP) - rayPos;
                float depth = length(toScene);
                float3 rayDir = toScene / depth;

                float2 rayToContainerInfo = rayBoxDst(boundsMin, boundsMax, rayPos, 1/rayDir);
                float dstToBox = rayToContainerInfo.x;
                float dstInsideBox = rayToContainerInfo.y;
                float3 entryPoint = rayPos + rayDir * dstToBox;

                // random starting offset (makes low-res results noisy rather than jagged)
                float randomOffset = SAMPLE_TEXTURE2D_LOD(BlueNoise, samplerBlueNoise, squareUV(uv*3), 0).r;
                randomOffset *= rayOffsetStrength;

                // phase function makes clouds brighter around the sun
                float cosAngle = dot(rayDir, sunDir);
                float phaseVal = lightingOn ? phase(cosAngle) : 1;

                float dstTravelled = randomOffset;
                float dstLimit = min(depth-dstToBox, dstInsideBox);

                float transmittance = 1;
                float lightEnergy = 0;
                [loop] for (int n = 0; n < 2048 && dstTravelled < dstLimit; n++) {
                    float3 p = entryPoint + rayDir * dstTravelled;
                    float density = sampleDensity(p);
                    if (density > 0) {
                        float lightTransmittance = lightingOn ? lightmarch(p) : 1;
                        lightEnergy += density * stepSize * transmittance * lightTransmittance * phaseVal;
                        transmittance *= exp(-density * stepSize * lightAbsorptionThroughCloud);
                        if (transmittance < 0.01) break;
                    }
                    dstTravelled += stepSize;
                }

                float3 cloudCol = lightEnergy * sunColor.rgb;
                float3 col = backgroundCol * transmittance + cloudCol;
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
