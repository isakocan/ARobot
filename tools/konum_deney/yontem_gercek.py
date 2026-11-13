"""Hile yöntemi: gerçek konumu döndürür (düzeneğin doğruluğunu kanıtlar; tüm hatalar ~0 olmalı)."""
import os

import numpy as np

from kayit import Kayit


class Yontem:
    def __init__(self, bilgi, kamera=None):
        gk = Kayit(bilgi["_kayit_klasoru"]).gercek_konum()
        self.t = gk["t_ms"]
        self.x, self.y, self.yon = gk["x_m"], gk["y_m"], gk["yon_derece"]

    def guncelle(self, kare_bgr, t_ms, komut):
        i = int(np.argmin(np.abs(self.t - t_ms)))
        return float(self.x[i]), float(self.y[i]), float(self.yon[i])
