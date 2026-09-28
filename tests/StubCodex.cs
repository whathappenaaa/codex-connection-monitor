// Offline protocol fixture. No account, files or network are accessed.
using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class StubCodex {
    static void Main() {
        string mode=Environment.GetEnvironmentVariable("MONITOR_TEST_CASE")??"success";
        var json=new JavaScriptSerializer();string line;
        while((line=Console.ReadLine())!=null){
            var request=json.Deserialize<Dictionary<string,object>>(line);object id;
            if(!request.TryGetValue("id",out id))continue;
            string method=Convert.ToString(request["method"]);object result=new {};
            if(method=="account/read")result=mode=="login"?new {account=(object)null}:new {account=(object)new {type="chatgpt",email="fixture@example.invalid"}};
            if(method=="account/rateLimits/read"){
                if(mode=="timeout")continue;
                if(mode=="unsupported"){Console.WriteLine(json.Serialize(new {id=id,error=new {code=-32601,message="Method not found"}}));continue;}
                result=new {rateLimitsByLimitId=new {codex=new {primary=new {usedPercent=25,windowDurationMins=300,resetsAt=1999999999}}}};
            }
            Console.WriteLine(json.Serialize(new {id=id,result=result}));
        }
    }
}
