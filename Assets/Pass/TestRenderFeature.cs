using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class TestRenderFeature : ScriptableRendererFeature
{
	TestRenderPass testRenderPass;
	public Material testRenderMat;
	RenderTargetHandle testRenderTexture;

	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		testRenderPass.Setup(renderer.cameraColorTarget, testRenderMat);
		renderer.EnqueuePass(testRenderPass);
	}

	public override void Create()
	{
		testRenderPass = new TestRenderPass(RenderPassEvent.BeforeRenderingPostProcessing);
	}
}
