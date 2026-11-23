using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class TestRenderPass : ScriptableRenderPass
{
	Material testRenderMat;
	int rtIdentifier = UnityEngine.Shader.PropertyToID("TempColorBuffer");
	RenderTargetIdentifier srcRTI;
	RenderTargetIdentifier destRTI;

	public TestRenderPass(RenderPassEvent renderPassEvent)
	{
		this.renderPassEvent = renderPassEvent;
	}

	public void Setup(RenderTargetIdentifier inSrcRTI, Material inMat)
	{
		srcRTI = inSrcRTI;
		destRTI = new RenderTargetIdentifier(rtIdentifier);
		testRenderMat = inMat;
	}

	public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
	{
		var cmd = CommandBufferPool.Get("TestRenderPass");

		RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;

		cmd.GetTemporaryRT(rtIdentifier, descriptor);
		cmd.Blit(srcRTI, destRTI);
		cmd.Blit(destRTI, srcRTI, testRenderMat);
		context.ExecuteCommandBuffer(cmd);
		cmd.ReleaseTemporaryRT(rtIdentifier);
		CommandBufferPool.Release(cmd);
	}
}
