import sys, json, os, cv2, numpy as np
sys.path.insert(0, r"C:/Users/PC/Desktop/ARobot/.claude/worktrees/stella-vslam-dense-mapping-5eff80/tools/konum")
import nokta_harita as nh
K = r"C:/Users/PC/Desktop/ARobot/kayitlar_25kas/robot/20261125_000545"
R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
g = cv2.imread(os.path.join(K, R[500]["jpg"]), cv2.IMREAD_GRAYSCALE)
pan = cv2.resize(nh.panorama(g), (1440, 416), interpolation=cv2.INTER_LINEAR).astype(np.float32) / 255.0
pan.tofile("pan_416x1440.bin"); np.save("pan_416x1440.npy", pan); print(pan.shape, pan.mean())
