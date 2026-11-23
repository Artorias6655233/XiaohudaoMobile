using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class EdgePass : ScriptableRenderPass
{
	Material _postMat;
	int rtIdentifier = UnityEngine.Shader.PropertyToID("TempColorBuffer");
	RenderTargetIdentifier _srcRTI;
	RenderTargetIdentifier _destRTI;

	public EdgePass(RenderPassEvent renderPassEvent)
	{
		this.renderPassEvent = renderPassEvent;
	}

	public void Setup(RenderTargetIdentifier srcRTI, Material postMat)
	{
		_srcRTI = srcRTI;
		_destRTI = new RenderTargetIdentifier(rtIdentifier);
		_postMat = postMat;
	}

	public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
	{
		var cmd = CommandBufferPool.Get("EdgePass");

		RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;

		cmd.GetTemporaryRT(rtIdentifier, descriptor);
		cmd.Blit(_srcRTI, _destRTI);
		cmd.Blit(_destRTI, _srcRTI, _postMat);
		context.ExecuteCommandBuffer(cmd);
		cmd.ReleaseTemporaryRT(rtIdentifier);
		CommandBufferPool.Release(cmd);
	}
}
