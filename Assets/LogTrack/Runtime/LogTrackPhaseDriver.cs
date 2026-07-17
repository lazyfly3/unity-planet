using UnityEngine;



/// <summary>

/// 按 Unity game update 驱动 LogTrack：FixedUpdate → Update → LateUpdate。

/// 逻辑帧在首个 FixedUpdate 开启，LateUpdate 结束关闭。

/// </summary>

[DefaultExecutionOrder(-1000)]

internal sealed class LogTrackPhaseDriver : MonoBehaviour

{

    private int m_frameIndex;

    private bool m_frameStarted;

    private bool m_exportHandled;

    private bool m_sessionStarted;



    private void Awake()

    {

        m_frameIndex = 0;

        m_frameStarted = false;

        m_exportHandled = false;

        m_sessionStarted = false;



        var ringBuffer = LogTrackSettings.DefaultRingBufferSize;

        FSPDebuger.TrackBufferSize = ringBuffer;

        FSPDebuger.BeginTrack(ringBuffer);

        m_sessionStarted = true;

        Application.quitting += ExportIfNeeded;

        Debug.Log($"LogTrack PhaseDriver 启动，RingBuffer={ringBuffer}");

    }



    private void FixedUpdate()

    {

        if (!FSPDebuger.EnableLogTrackInternal) return;



        if (!m_frameStarted)

        {

            m_frameIndex++;

            FSPDebuger.EnterTrackFrame(m_frameIndex);

            m_frameStarted = true;

        }



        FSPDebuger.SetPhase(LogTrackPhase.FixedUpdate);

        FSPDebuger.ResetPhaseDepth();

    }



    private void Update()

    {

        if (!FSPDebuger.EnableLogTrackInternal) return;



        if (!m_frameStarted)

        {

            m_frameIndex++;

            FSPDebuger.EnterTrackFrame(m_frameIndex);

            m_frameStarted = true;

        }



        FSPDebuger.SetPhase(LogTrackPhase.Update);

        FSPDebuger.ResetPhaseDepth();

    }



    private void LateUpdate()

    {

        if (FSPDebuger.EnableLogTrackInternal)

        {

            if (!m_frameStarted)

            {

                m_frameIndex++;

                FSPDebuger.EnterTrackFrame(m_frameIndex);

                m_frameStarted = true;

            }



            FSPDebuger.SetPhase(LogTrackPhase.LateUpdate);

            FSPDebuger.ResetPhaseDepth();

        }



        m_frameStarted = false;

    }



    private void OnDestroy()

    {

        Application.quitting -= ExportIfNeeded;

        ExportIfNeeded();

    }



    private void ExportIfNeeded()

    {

        if (m_exportHandled || !m_sessionStarted)

        {

            return;

        }



        m_exportHandled = true;

        LogTrackRecordingExport.ExportIfNeeded();

    }

}


