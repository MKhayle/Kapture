﻿namespace ACT_FFXIV_Kapture.Config.Model
{
	public class HTTP
	{
		public bool HTTPEnabled { get; set; } = false;
		public string Endpoint { get; set; }
		public string CustomJson { get; set; }
	}
}