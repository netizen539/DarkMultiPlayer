using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class ScenarioWorker
    {
        public bool workerEnabled;
        private Client parent;
        private Queue<ScenarioEntry> scenarioQueue;
        private bool blockScenarioDataSends;
        private bool loadedScience;
        private float lastScenarioSendTime;
        private const float SEND_SCENARIO_DATA_INTERVAL = 30f;

        public ScenarioWorker(Client parent)
        {
            this.parent = parent;
            Reset();
        }

        public void Update()
        {
            if (workerEnabled && !blockScenarioDataSends)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - lastScenarioSendTime) > SEND_SCENARIO_DATA_INTERVAL)
                {
                    lastScenarioSendTime = UnityEngine.Time.realtimeSinceStartup;
                    SendScenarioModules();
                }
            }
        }

        private void SendScenarioModules()
        {
            List<string> scenarioName = new List<string>();
            List<string> scenarioData = new List<string>();
            foreach (ProtoScenarioModule psm in HighLogic.CurrentGame.scenarios)
            {
                //Skip sending science data in sandbox mode (If this can even happen?)
                if (psm != null ? (psm.moduleName != null && psm.moduleRef != null) : false)
                {
                    if (!(psm.moduleName == "ResearchAndDevelopment" && parent.gameMode == GameMode.SANDBOX))
                    {
                        ConfigNode scenarioNode = new ConfigNode();
                        psm.moduleRef.Save(scenarioNode);
                        //Yucky.
                        string tempFile = Path.GetTempFileName();
                        scenarioNode.Save(tempFile);
                        using (StreamReader sr = new StreamReader(tempFile))
                        {
                            scenarioName.Add(psm.moduleName);
                            scenarioData.Add(sr.ReadToEnd());
                        }
                        File.Delete(tempFile);
                    }
                }
            }
            if (scenarioName.Count > 0)
            {
                DarkLog.Debug("Sending " + scenarioName.Count + " scenario modules");
                parent.networkWorker.SendScenarioModuleData(scenarioName.ToArray(), scenarioData.ToArray());
            }
        }

        public void LoadScenarioDataIntoGame()
        {
            while (scenarioQueue.Count > 0)
            {
                LoadScenarioData(scenarioQueue.Dequeue());
            }
            if (!loadedScience && parent.gameMode == GameMode.CAREER)
            {
                DarkLog.Debug("Creating new science data");
                ConfigNode newNode = GetBlankResearchAndDevelopmentNode();
                ProtoScenarioModule newModule = new ProtoScenarioModule(newNode);
                try
                {
                    HighLogic.CurrentGame.scenarios.Add(newModule);
                    newModule.Load(ScenarioRunner.fetch);
                }
                catch
                {
                    DarkLog.Debug("Error loading new science data!");
                    blockScenarioDataSends = true;
                }
            }
        }

        //Would be nice if we could ask KSP to do this for us...
        private ConfigNode GetBlankResearchAndDevelopmentNode()
        {
            ConfigNode newNode = new ConfigNode();
            newNode.AddValue("name", "ResearchAndDevelopment");
            newNode.AddValue("scene", "5, 6, 7, 8, 9");
            newNode.AddValue("sci", "0");
            newNode.AddNode("Tech");
            newNode.GetNode("Tech").AddValue("id", "start");
            newNode.GetNode("Tech").AddValue("state", "Available");
            newNode.GetNode("Tech").AddValue("part", "mk1pod");
            newNode.GetNode("Tech").AddValue("part", "liquidEngine");
            newNode.GetNode("Tech").AddValue("part", "solidBooster");
            newNode.GetNode("Tech").AddValue("part", "fuelTankSmall");
            newNode.GetNode("Tech").AddValue("part", "trussPiece1x");
            newNode.GetNode("Tech").AddValue("part", "longAntenna");
            newNode.GetNode("Tech").AddValue("part", "parachuteSingle");
            return newNode;
        }

        public void LoadScenarioData(ScenarioEntry entry)
        {
            if (entry.scenarioName == "ResearchAndDevelopment" && parent.gameMode != GameMode.CAREER)
            {
                DarkLog.Debug("Skipping loading career mode data in sandbox");
                return;
            }
            if (entry.scenarioName == "ResearchAndDevelopment" && parent.gameMode == GameMode.CAREER)
            {
                loadedScience = true;
            }
            //Don't stare directly at the next 7 lines - It's bad for your eyes.
            string tempFile = Path.GetTempFileName();
            using (StreamWriter sw = new StreamWriter(tempFile))
            {
                sw.Write(entry.scenarioData);
            }
            ConfigNode scenarioNode = ConfigNode.Load(tempFile);
            File.Delete(tempFile);
            if (scenarioNode == null)
            {
                DarkLog.Debug(entry.scenarioName + " scenario data failed to create a ConfigNode!");
                blockScenarioDataSends = true;
                return;
            }
            bool loaded = false;
            foreach (ProtoScenarioModule psm in HighLogic.CurrentGame.scenarios)
            {
                if (psm.moduleName == entry.scenarioName)
                {
                    DarkLog.Debug("Loading existing " + entry.scenarioName + " scenario module");
                    try
                    {
                        if (psm.moduleRef == null)
                        {
                            DarkLog.Debug("Fixing null scenario module!");
                            psm.moduleRef = new ScenarioModule();
                        }
                        psm.moduleRef.Load(scenarioNode);
                    }
                    catch (Exception e)
                    {
                        DarkLog.Debug("Error loading " + entry.scenarioName + " scenario module, Exception: " + e);
                        blockScenarioDataSends = true;
                    }
                    loaded = true;
                }
            }
            if (!loaded)
            {
                DarkLog.Debug("Loading new " + entry.scenarioName + " scenario module");
                ProtoScenarioModule scenarioModule = new ProtoScenarioModule(scenarioNode);
                try
                {
                    HighLogic.CurrentGame.scenarios.Add(scenarioModule);
                    scenarioModule.Load(ScenarioRunner.fetch);
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Error loading " + entry.scenarioName + " scenario module, Exception: " + e);
                    blockScenarioDataSends = true;
                }
            }
        }

        public void QueueScenarioData(string scenarioName, string scenarioData)
        {
            ScenarioEntry entry = new ScenarioEntry();
            entry.scenarioName = scenarioName;
            entry.scenarioData = scenarioData;
            scenarioQueue.Enqueue(entry);
        }

        public void Reset()
        {
            workerEnabled = false;
            loadedScience = false;
            scenarioQueue = new Queue<ScenarioEntry>();
            blockScenarioDataSends = false;
            lastScenarioSendTime = 0f;
        }
    }

    public class ScenarioEntry
    {
        public string scenarioName;
        public string scenarioData;
    }
}

