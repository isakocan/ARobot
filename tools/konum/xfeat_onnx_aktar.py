import torch
m = torch.hub.load("verlab/accelerated_features", "XFeat", pretrained=True, top_k=1500, trust_repo=True, verbose=False)
net = m.net.eval().cpu()
x = torch.rand(1, 1, 416, 1440)
with torch.no_grad():
    o = net(x); print([t.shape for t in o])
torch.onnx.export(net, x, "xfeat_416x1440.onnx", opset_version=11, dynamo=False, input_names=["img"], output_names=["feats", "kpts", "heat"])
import os; print(os.path.getsize("xfeat_416x1440.onnx") // 1024, "KB")
