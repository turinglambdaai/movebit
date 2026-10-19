import AppKit
import ApplicationServices
import Foundation
enum E: Error { case failure(String) }
func attr(_ e:AXUIElement,_ n:String)->CFTypeRef?{var v:CFTypeRef?;return AXUIElementCopyAttributeValue(e,n as CFString,&v) == .success ? v:nil}
func str(_ e:AXUIElement,_ n:String)->String{attr(e,n) as? String ?? ""}
func children(_ e:AXUIElement)->[AXUIElement]{attr(e,kAXChildrenAttribute) as? [AXUIElement] ?? []}
func walk(_ e:AXUIElement)->[AXUIElement]{[e]+children(e).flatMap(walk)}
func contains(_ e:AXUIElement,_ f:String)->Bool{walk(e).contains{n in [kAXTitleAttribute,kAXValueAttribute,kAXDescriptionAttribute].contains{str(n,$0).contains(f)}}}
func wait<T>(_ probe:()->T?) throws->T{let d=Date().addingTimeInterval(90);while Date()<d{if let v=probe(){return v};Thread.sleep(forTimeInterval:.05)};throw E.failure("timeout")}
func snap(_ e:AXUIElement,_ depth:Int=0)->[String:Any]{var r:[String:Any]=["role":str(e,kAXRoleAttribute),"title":str(e,kAXTitleAttribute),"value":str(e,kAXValueAttribute),"label":str(e,kAXDescriptionAttribute)];if depth<10{r["children"]=children(e).map{snap($0,depth+1)}};return r}
let a=CommandLine.arguments;let pid=Int32(a[a.firstIndex(of:"--pid")!+1])!;let out=URL(fileURLWithPath:a[a.firstIndex(of:"--output")!+1],isDirectory:true);let app=AXUIElementCreateApplication(pid)
do{guard AXIsProcessTrusted() else{throw E.failure("accessibility unavailable")};let window:AXUIElement=try wait{(attr(app,kAXWindowsAttribute) as? [AXUIElement])?.first};_ = try wait{contains(window,"Active today") ? true:nil};try JSONSerialization.data(withJSONObject:snap(app),options:.prettyPrinted).write(to:out.appendingPathComponent("accessibility.json"));try JSONSerialization.data(withJSONObject:["platform":"macos","actions":["launch:isolated-config"],"assertions":["packaged SwiftUI window activated","RVT1 backend activity state reached native accessibility tree"]],options:.prettyPrinted).write(to:out.appendingPathComponent("interaction-trace.json"));guard NSRunningApplication(processIdentifier:pid)?.terminate()==true else{throw E.failure("termination rejected")}}catch{try? JSONSerialization.data(withJSONObject:snap(app),options:.prettyPrinted).write(to:out.appendingPathComponent("accessibility-failure.json"));FileHandle.standardError.write(Data("MoveBit acceptance failed: \(error)\n".utf8));exit(1)}
