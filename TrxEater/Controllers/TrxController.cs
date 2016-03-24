using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Newtonsoft.Json;
using TrxEater.Models;
using TrxEater.Utilities;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Management.Automation;
using System.IO;
using System.IO.Compression;
using System.Management.Automation.Runspaces;
using Microsoft.TeamFoundation.Build.Client;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.TestManagement.Client;

namespace TrxEater.Controllers
{
    public class TrxController : ApiController
    {
        private const string TcmPath = "C:\\Program Files (x86)\\Microsoft Visual Studio 14.0\\Common7\\IDE";
        private static string _dllPath;
        private static string _trxPath;
        private const string Collection = "http://prd-tfsfpv01:8080/tfs/Collection01";
        private static string _ProjectName;
        private static string _runowner;
      
        [Route("upload"),HttpPost]
        public async Task<HttpResponseMessage> RedirectFile(string projectName, string runOwner)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("",Encoding.UTF8, "application/json");
            const string savePath = "~/App_Data";
            _ProjectName = projectName;
            _runowner = runOwner;

            List<UploadedFileInfo> files;
            try
            {
                files = await Request.SaveFiles(HttpContext.Current.Server.MapPath(savePath));
            }
            catch (HttpResponseException e)
            {
                return e.Response;
            }
            catch (Exception e)
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, e);
            }

            if (files == null || !files.Any())
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "No files attached");
            }

            UploadedFileInfo file;
            try
            {
                file = files.Single();
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "Endpoint only accepts one file");
            }
            string uploadedFilePath = file.LocalFileName;

            try
            {
               PostToMtm(uploadedFilePath);
            }
            catch (HttpResponseException ex)
            {
                return ex.Response;
            }
           

            response.Content =  new StringContent(response.Content.ToString());
            return response;
        }


        private void PostToMtm(string uploadedFilePath)
        {
            
            //upzip the folder
            UnZipFile(uploadedFilePath);
            
            //get Test Plan ID
            string testPlanId = RunTestPlanScript(TcmPath);
            
            //update the build number
            updateBuildNumber(int.Parse(testPlanId));

            //Get Test Suite ID
            string testSuiteId = RunTestSuiteScript(TcmPath,testPlanId);

            //Get Test Config ID
            string testConfigId = RunTestConfigScript(TcmPath);
            
            //Updtae the Current Test Cases and Create Unexit Ones
            UpdateOrCreateTestCase(testSuiteId);

            //Publish the Test Results
            PublishToMtm(testSuiteId, testConfigId, _trxPath);

        }

        private String RunTestPlanScript(string tcmPath)
        {
            HttpResponseMessage msg;
            string s = "TCM Exists";
            Runspace runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();
            runspace.SessionStateProxy.SetVariable("path", tcmPath);
            runspace.SessionStateProxy.SetVariable("collection", Collection);
            runspace.SessionStateProxy.SetVariable("teamproject", _ProjectName);
            
            Pipeline pipeline = runspace.CreatePipeline();
            if (!Directory.Exists(tcmPath))
            {
                s = "TCM Not Exists";
            }
            string Pathcmd = "cd $path";
            string testplanCommand = "./tcm plans /list /collection:$collection /teamproject:$teamproject";
            pipeline.Commands.AddScript(Pathcmd);
            pipeline.Commands.AddScript(testplanCommand);

            Collection<PSObject> testPlansResult = pipeline.Invoke();//Execute the ps script

            if (pipeline.Error.Count > 0)
            {
                s = s + "Powershell errors in the test plan";
                var error = pipeline.Error.Read() as Collection<ErrorRecord>;
                if (error != null)
                {
                    foreach (ErrorRecord er in error)
                    { 
                        s = s + er.Exception.Message;
                    }
                }
            }
            try
            {
                string testPlanId = splitArrayOfString(testPlansResult);
 
                runspace.Close();

                return testPlanId;
            }
            catch (Exception ex)
            {

                msg = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    ReasonPhrase = Collection + _ProjectName,
                    Content = new StringContent(s)
                };
                throw new HttpResponseException(msg);
            }   
            
        }

        private String RunTestSuiteScript(string tcmPath, string testPlanId)
        {
            HttpResponseMessage msg;
            string s = "TCM Exists";
            Runspace runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();
            runspace.SessionStateProxy.SetVariable("path", tcmPath);
            runspace.SessionStateProxy.SetVariable("collection", Collection);
            runspace.SessionStateProxy.SetVariable("teamproject", _ProjectName);

            Pipeline pipeline = runspace.CreatePipeline();

            string Pathcmd = "cd $path";

            runspace.SessionStateProxy.SetVariable("testPlanID", testPlanId);

            string testSuitesCommand = "./tcm suites /list /planid:$testPlanID /collection:$collection /teamproject:$teamproject";
            pipeline.Commands.AddScript(Pathcmd);
            pipeline.Commands.AddScript(testSuitesCommand);
            Collection<PSObject> testSuitesResult = pipeline.Invoke();//Execute the ps script

            if (pipeline.Error.Count > 0)
            {
                s = s + "Powershell errors in the test Suite";
                var error = pipeline.Error.Read() as Collection<ErrorRecord>;
                if (error != null)
                {
                    foreach (ErrorRecord er in error)
                    {
                        s = s + er.Exception.Message;
                    }
                }
            }
            foreach (PSObject str in testSuitesResult)
            {
                s = s + str.ToString();
            }
            try
            {
                string testSuiteId = splitArrayOfString(testSuitesResult);

                runspace.Close();

                return testSuiteId;
            }
            catch (Exception ex)
            {

                msg = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    ReasonPhrase = Collection + _ProjectName,
                    Content = new StringContent(s)
                };
                throw new HttpResponseException(msg);
            }
        }

        private String RunTestConfigScript(string tcmPath)
        {
            HttpResponseMessage msg;
            string s = "TCM Exists";

            Runspace runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();
            runspace.SessionStateProxy.SetVariable("path", tcmPath);
            runspace.SessionStateProxy.SetVariable("collection", Collection);
            runspace.SessionStateProxy.SetVariable("teamproject", _ProjectName);

            Pipeline pipeline = runspace.CreatePipeline();

            string Pathcmd = "cd $path";

            string testConfigCommand = "./tcm configs /list /collection:$collection /teamproject:$teamproject";
            pipeline.Commands.AddScript(Pathcmd);
            pipeline.Commands.AddScript(testConfigCommand);
            Collection<PSObject> testConfigResult = pipeline.Invoke();//Execute the ps script

            if (pipeline.Error.Count > 0)
            {
                s = s + "Powershell errors in the test Config";
                var error = pipeline.Error.Read() as Collection<ErrorRecord>;
                if (error != null)
                {
                    foreach (ErrorRecord er in error)
                    {
                        s = s + er.Exception.Message;
                    }
                }
            }
            try
            {
                string testConfigId = splitArrayOfString(testConfigResult);

                runspace.Close();

                return testConfigId;
            }
            catch (Exception ex)
            {

                msg = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    ReasonPhrase = Collection + _ProjectName,
                    Content = new StringContent(s)
                };
                throw new HttpResponseException(msg);
            }
        }

        private void PublishToMtm(String testSuiteId, String testConfigId, string filePath)
        {
            HttpResponseMessage msg;
            string s = "TCM Exists";

            Runspace runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();
            runspace.SessionStateProxy.SetVariable("path", TcmPath);
            runspace.SessionStateProxy.SetVariable("collection", Collection);
            runspace.SessionStateProxy.SetVariable("teamproject", _ProjectName);
            runspace.SessionStateProxy.SetVariable("filePath", filePath);
            runspace.SessionStateProxy.SetVariable("testSuiteID", testSuiteId);
            runspace.SessionStateProxy.SetVariable("testConfigID", testConfigId);
            runspace.SessionStateProxy.SetVariable("resultowner", _runowner);

            Pipeline pipeline = runspace.CreatePipeline();

            string Pathcmd = "cd $path";

            string publishCommand = "./tcm run /publish /suiteid:$testSuiteID /configid:$testConfigID /resultowner:$resultowner /resultsfile:$filePath /collection:$collection /teamproject:$teamproject";
            pipeline.Commands.AddScript(Pathcmd);
            pipeline.Commands.AddScript(publishCommand);

            try
            {
                pipeline.Invoke();//Execute the ps script

                if (pipeline.Error.Count > 0)
                {
                    var error = pipeline.Error.Read() as Collection<ErrorRecord>;
                    if (error != null)
                    {
                        foreach (ErrorRecord er in error)
                        {
                            s = s + er.Exception.Message;
                        }
                    }
                }
                runspace.Close();
            }
            catch (Exception ex)
            {

                msg = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    ReasonPhrase = Collection + _ProjectName,
                    Content = new StringContent(s)
                };
                throw new HttpResponseException(msg);
            }

        }

        private string splitArrayOfString(Collection<PSObject> results)
        {
            List<int> compareArray = new List<int>();
            for(int i = 3; i < results.Count; i++)
            {
                string resultElement = results[i].ToString().Split(Convert.ToChar(" "))[0];
                compareArray.Add(Int32.Parse(resultElement));
            }

            return compareArray.Max().ToString();
        }

        private void UpdateOrCreateTestCase(String testSuiteId)
        {
            Runspace runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();
            runspace.SessionStateProxy.SetVariable("path", TcmPath);
            runspace.SessionStateProxy.SetVariable("collection", Collection);
            runspace.SessionStateProxy.SetVariable("teamproject", _ProjectName);
            runspace.SessionStateProxy.SetVariable("storage", _dllPath);
            runspace.SessionStateProxy.SetVariable("testSuiteID", testSuiteId);

            Pipeline pipeline = runspace.CreatePipeline();

            string Pathcmd = "cd $path";

            string UpdateTestCasesCommand = "./tcm testcase /collection:$collection /teamproject:$teamproject /import /storage:$storage /syncsuite:$testSuiteID";
            pipeline.Commands.AddScript(Pathcmd);
            pipeline.Commands.AddScript(UpdateTestCasesCommand);

            pipeline.Invoke();//Execute the ps script

            runspace.Close();
         }

        private void UnZipFile(string uploadedFilePath)
        {
            using (ZipArchive archive = ZipFile.Open(uploadedFilePath, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    FileInfo fi = new FileInfo(uploadedFilePath);
                    string folderName = Path.GetFileNameWithoutExtension(fi.Name);
                    String folderPath = fi.Directory.ToString() + "\\" + folderName;
                    if(!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                    }
                    if (fi.Directory != null)
                    {
                        entry.ExtractToFile(folderPath + "\\" + entry.Name);
                        if (entry.Name.Contains(".trx"))
                        {
                            _trxPath = folderPath + "\\" + entry.Name;
                        }
                        else if (entry.Name.Contains(".dll"))
                        {
                            _dllPath = folderPath + "\\" + entry.Name;
                        }
                    }
                    
                }
            }
        }

        private void updateBuildNumber(int testPlanId)
        {
            //Define the variables
            string serverurl = Collection;
            Uri configurationServerUri = new Uri(serverurl);
            string project = _ProjectName;
            int myPlansId = testPlanId;

            try
            {
                //Get the build uri needed from build number
                TfsTeamProjectCollection tfsserv = new TfsTeamProjectCollection(configurationServerUri);
                Uri buildUri = null;
                
                
                IBuildServer buildServer = (IBuildServer)tfsserv.GetService(typeof(IBuildServer));

                IBuildDetailSpec detailSpec = buildServer.CreateBuildDetailSpec(project);
                detailSpec.MaxBuildsPerDefinition = 1;
                detailSpec.QueryOrder = BuildQueryOrder.FinishTimeDescending;
                IBuildQueryResult results = buildServer.QueryBuilds(detailSpec);
                if (results.Builds.Length == 1)
                {
                    IBuildDetail detail = results.Builds[0];
                    buildUri = detail.Uri;
                }

                //Update the test plan
                ITestManagementTeamProject proj = GetProject(serverurl, project);
                ITestPlan plan = proj.TestPlans.Find(myPlansId);
                Console.WriteLine("Test Plan: {0}", plan.Name);
                Console.WriteLine("Plan ID: {0}", plan.Id);
                Console.WriteLine("Previous Build Uri: {0}", plan.PreviousBuildUri);
                Console.WriteLine("Build Currently In Use: {0}", plan.BuildNumber);
                plan.BuildUri = buildUri;
                Console.WriteLine("\n Build updated in Test Plan \n");
                Console.WriteLine("Build Currently In Use: {0}", plan.BuildNumber);
                plan.Save();
            }
            catch (Exception e)
            {
                Console.WriteLine("\n There was an error \n");
                Console.WriteLine(e);
            }
        }


        static ITestManagementTeamProject GetProject(string serverUrl,
            string project)
        {
            TfsTeamProjectCollection tfs = new TfsTeamProjectCollection(TfsTeamProjectCollection.GetFullyQualifiedUriForName(serverUrl));
            ITestManagementService tms = tfs.GetService<ITestManagementService>();

            return tms.GetTeamProject(project);
        }
    }
}
