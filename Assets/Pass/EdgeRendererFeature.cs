using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class EdgeRendererFeature : ScriptableRendererFeature
{
	EdgePass edgePass;
	public Material edgeHighLightMat;
	RenderTargetHandle edgeHighLightTexture;

	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		edgePass.Setup(renderer.cameraColorTarget, edgeHighLightMat);
		renderer.EnqueuePass(edgePass);
	}

	public override void Create()
	{
		edgePass = new EdgePass(RenderPassEvent.AfterRenderingPostProcessing);
	}
}
