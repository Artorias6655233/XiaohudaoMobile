using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DrawEdgeBGRendererFeature : ScriptableRendererFeature
{
	DrawEdgeBGPass drawEdgeBGPass;
	RenderTargetHandle drawEdgeBGTexture;
	public Material drawEdgeBGMat;

	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		drawEdgeBGPass.Setup(renderingData.cameraData.cameraTargetDescriptor,
			drawEdgeBGTexture);
		renderer.EnqueuePass(drawEdgeBGPass);
	}

	public override void Create()
	{
		LayerMask layerMask = 0 << 0 | 0 << 1 | 0 << 2 | 0 << 4 | 0 << 5 | 1 << 6;
		drawEdgeBGPass = new DrawEdgeBGPass(RenderPassEvent.BeforeRenderingPrePasses,
			drawEdgeBGMat,
			RenderQueueRange.all,
			layerMask);

		/*LayerMask testLayerMask = 1 << 0 | 0 << 1 | 0 << 2 | 0 << 4 | 0 << 5 | 1 << 6;
		drawEdgeBGPass = new DrawEdgeBGPass(RenderPassEvent.BeforeRenderingPrePasses,
			drawEdgeBGMat,
			RenderQueueRange.all,
			-1);*/

		drawEdgeBGTexture.Init("_DrawEdgeBGTexture");
	}
}
