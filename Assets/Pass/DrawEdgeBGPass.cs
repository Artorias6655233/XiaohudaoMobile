using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DrawEdgeBGPass : ScriptableRenderPass
{
	private string _profilerTag = "DrawEdgeBGPass";
	private ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
	private Material _drawEdgeBGMat;
	private FilteringSettings _filterSettings;
	private ProfilingSampler _profilingSampler;
	private RenderTextureDescriptor _descriptor;
	private RenderTargetHandle _drawEdgeBGRTH;

	public DrawEdgeBGPass(RenderPassEvent renderPassEvent,
		Material drawEdgeBGMat,
		RenderQueueRange renderQueueRange,
		LayerMask layerMask)
	{
		this.renderPassEvent = renderPassEvent;
		this._drawEdgeBGMat = drawEdgeBGMat;
		_filterSettings = new FilteringSettings(renderQueueRange, layerMask);
		_profilingSampler = new ProfilingSampler(_profilerTag);
	}

	public void Setup(RenderTextureDescriptor descriptor, RenderTargetHandle drawEdgeBGRTH)
	{
		_descriptor = descriptor;
		_drawEdgeBGRTH = drawEdgeBGRTH;
	}

	public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
	{
		cmd.GetTemporaryRT(_drawEdgeBGRTH.id, _descriptor, FilterMode.Point);
		ConfigureTarget(_drawEdgeBGRTH.Identifier());
		ConfigureClear(ClearFlag.All, Color.black);
	}

	public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
	{
		CommandBuffer cmd = CommandBufferPool.Get(_profilerTag);
		using(new ProfilingScope(cmd, _profilingSampler))
		{
			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
			var drawSettings = CreateDrawingSettings(_shaderTagId, ref renderingData, sortFlags);
			drawSettings.perObjectData = PerObjectData.None;
			drawSettings.overrideMaterial = _drawEdgeBGMat;
			context.DrawRenderers(renderingData.cullResults,
				ref drawSettings,
				ref _filterSettings);
			cmd.SetGlobalTexture("_DrawEdgeBGTexture", _drawEdgeBGRTH.id);
		}
		context.ExecuteCommandBuffer(cmd);
		CommandBufferPool.Release(cmd);
	}

	public override void FrameCleanup(CommandBuffer cmd)
	{
		if (_drawEdgeBGRTH != RenderTargetHandle.CameraTarget)
		{
			cmd.ReleaseTemporaryRT(_drawEdgeBGRTH.id);
			_drawEdgeBGRTH = RenderTargetHandle.CameraTarget;
		}
	}

}
