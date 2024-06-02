using Godot;
using Godot.Collections;
using Mattoha.Core.Demo;
using Mattoha.Core;
using Mattoha.Core.Utils;
using System;
using Mattoha.Misc;

namespace Mattoha.Nodes;
public partial class MattohaServer : Node
{
	[Export] public MattohaServerMiddleware MiddlewareNode { get; set; }
	public Dictionary<long, Dictionary<string, Variant>> Players { get; set; } = new();
	public Dictionary<int, Dictionary<string, Variant>> Lobbies { get; set; } = new();
	public Dictionary<int, Array<Dictionary<string, Variant>>> SpawnedNodes { get; set; } = new();
	public Dictionary<int, Array<Dictionary<string, Variant>>> RemovedSceneNodes { get; set; } = new();


	public Node GameHolder => GetNode("/root/GameHolder");

	private MattohaSystem _system;

	public override void _Ready()
	{
		_system = GetParent<MattohaSystem>();
		_system.ServerRpcRecieved += OnServerRpcRecieved;
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		base._Ready();
	}

	private void OnPeerDisconnected(long id)
	{
#if MATTOHA_SERVER
		if (Multiplayer.IsServer())
		{
			RemovePlayerFromLobby(id);
			UnRegisterPlayer(id);
		}
#endif
	}

	private void OnPeerConnected(long id)
	{
#if MATTOHA_SERVER
		if (Multiplayer.IsServer())
		{
			RegisterPlayer(id);
		}
#endif
	}


	private void RegisterPlayer(long id)
	{
#if MATTOHA_SERVER
		if (!Players.ContainsKey(id))
		{
			MiddlewareNode.BeforeRegisterPlayer(id);
			var player = new Dictionary<string, Variant>
				{
					{ MattohaPlayerKeys.Id, id },
					{ MattohaPlayerKeys.Username, $"Player{id}" },
					{ MattohaPlayerKeys.JoinedLobbyId, 0 },
					{ MattohaPlayerKeys.TeamId, 0 },
					{ MattohaPlayerKeys.IsInGamae, false },
					{ MattohaPlayerKeys.PrivateProps, new Array<string>() },
					{ MattohaPlayerKeys.ChatProps, new Array<string>(){ MattohaPlayerKeys.Id, MattohaPlayerKeys.Username } },
				};
			Players.Add(id, player);
			MiddlewareNode.AfterRegisterPlayer(id);
			_system.SendReliableClientRpc(id, nameof(ClientRpc.RegisterPlayer), player);
		}
#endif
	}

	private void UnRegisterPlayer(long id)
	{
#if MATTOHA_SERVER
		if (Players.ContainsKey(id))
		{
			MiddlewareNode.BeforeUnRegisterPlayer(id);
			Players.Remove(id);
			MiddlewareNode.AfterUnRegisterPlayer(id);
		}
#endif
	}


	public Dictionary<string, Variant> FindSpawnedNode(Dictionary<string, Variant> payload)
	{
		var lobbyId = MattohaSystem.ExtractLobbyId(payload[MattohaSpawnKeys.ParentPath].ToString());
		if (!SpawnedNodes.ContainsKey(lobbyId))
			return null;
		var nodes = SpawnedNodes[lobbyId];
		foreach (var node in nodes)
		{
			if (
				node[MattohaSpawnKeys.ParentPath].ToString() == payload[MattohaSpawnKeys.ParentPath].ToString() &&
				node[MattohaSpawnKeys.NodeName].ToString() == payload[MattohaSpawnKeys.NodeName].ToString()
			)
			{
				return node;
			}
		}
		return null;
	}


	public Dictionary<string, Variant> FindRemovedSceneNode(Dictionary<string, Variant> payload)
	{
		var lobbyId = MattohaSystem.ExtractLobbyId(payload[MattohaSpawnKeys.ParentPath].ToString());
		if(!RemovedSceneNodes.ContainsKey(lobbyId))
			return null;
		var nodes = RemovedSceneNodes[lobbyId];
		foreach (var node in nodes)
		{
			if (
				node[MattohaSpawnKeys.ParentPath].ToString() == payload[MattohaSpawnKeys.ParentPath].ToString() &&
				node[MattohaSpawnKeys.NodeName].ToString() == payload[MattohaSpawnKeys.NodeName].ToString()
			)
			{
				return node;
			}
		}
		return null;
	}


	public Dictionary<string, Variant> GetPlayer(long playerId)
	{
		if (!Players.TryGetValue(playerId, out var player))
			return null;
		return player;
	}

	public Dictionary<string, Variant> GetPlayerLobby(long playerId)
	{
		if (!Players.TryGetValue(playerId, out var player))
			return null;

		if (!Lobbies.TryGetValue(player[MattohaPlayerKeys.JoinedLobbyId].AsInt32(), out var lobby))
			return null;

		return lobby;
	}

	public Array<Dictionary<string, Variant>> GetLobbyPlayers(int lobbyId)
	{
		var players = new Array<Dictionary<string, Variant>>();
		foreach (var pl in Players.Values)
		{
			if (pl[MattohaPlayerKeys.JoinedLobbyId].AsInt32() == lobbyId)
			{
				players.Add(pl);
			}
		}
		return players;
	}


	public Array<Dictionary<string, Variant>> GetLobbyPlayersSecured(int lobbyId)
	{
		var players = new Array<Dictionary<string, Variant>>();
		foreach (var pl in Players.Values)
		{
			if (pl[MattohaPlayerKeys.JoinedLobbyId].AsInt32() == lobbyId)
			{
				players.Add(MattohaUtils.ToSecuredDict(pl));
			}
		}
		return players;
	}


	public void SendRpcForPlayersInLobby(int lobbyId, string methodName, Dictionary<string, Variant> payload, bool secureDict = false, long superPeer = 0, long ignorePeer = 0)
	{
		var players = GetLobbyPlayers(lobbyId);
		foreach (var player in players)
		{
			var playerId = player[MattohaPlayerKeys.Id].AsInt64();
			if (playerId == ignorePeer)
				continue;
			if (superPeer != 0 && playerId == superPeer)
			{
				_system.SendReliableClientRpc(playerId, methodName, payload);
			}
			else if (secureDict)
			{
				_system.SendReliableClientRpc(playerId, methodName, MattohaUtils.ToSecuredDict(payload));
			}
			else
			{
				_system.SendReliableClientRpc(playerId, methodName, payload);
			}
		}
	}


	private long CalculateLobbyXPosition()
	{
		// because adding the lobby node is done (after) adding lobby details to lobbies dictionary,
		// we wil substract the last added lobby (current lobby)
		// this will lead to start positioning lobbies from 0 to LobbySize * n
		// if we removed "-1" it will start positioning from lobbySize to LobbySize * n
		return (Lobbies.Count - 1) * _system.LobbySize;
	}


	private void RpcSetPlayerData(Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		if (!Players.TryGetValue(sender, out var player))
			return;
		
		var result = MiddlewareNode.BeforeSetPlayerData(payload, sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.SetPlayerDataFailed), result);

        foreach (var pair in payload)
		{
			if (MattohaPlayerKeys.FreezedProperties.Contains(pair.Key))
				continue;
			player[pair.Key] = pair.Value;
		}
		
		MiddlewareNode.AfterSetPlayerData(payload, sender);
		
		var lobbyId = player[MattohaPlayerKeys.JoinedLobbyId].AsInt32();
		_system.SendReliableClientRpc(sender, nameof(ClientRpc.SetPlayerData), player);
		SendRpcForPlayersInLobby(lobbyId, nameof(ClientRpc.JoinedPlayerUpdated), player, true);
#endif
	}


	private void RpcCreateLobby(Dictionary<string, Variant> lobbyData, long sender)
	{
#if MATTOHA_SERVER
		if (!Players.TryGetValue(sender, out var player))
			return;
		var lobbyId = Lobbies.Count + 1;
		lobbyData[MattohaLobbyKeys.Id] = lobbyId;
		lobbyData[MattohaLobbyKeys.OwnerId] = sender;
		lobbyData[MattohaLobbyKeys.PlayersCount] = 1;
		lobbyData[MattohaLobbyKeys.IsGameStarted] = false;
		if (lobbyData[MattohaLobbyKeys.PrivateProps].Obj == null)
		{
			lobbyData[MattohaLobbyKeys.PrivateProps] = new Array<string>();
		}
		var maxPlayers = _system.MaxPlayersPerLobby;
		if (lobbyData.ContainsKey(MattohaLobbyKeys.MaxPlayers))
		{
			maxPlayers = Math.Min(lobbyData[MattohaLobbyKeys.MaxPlayers].AsInt32(), _system.MaxPlayers);
		}
		lobbyData[MattohaLobbyKeys.MaxPlayers] = maxPlayers;
		var result = MiddlewareNode.BeforeCreateLobby(lobbyData, sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.CreateLobbyFailed), result);

		player[MattohaPlayerKeys.JoinedLobbyId] = lobbyId;
		Lobbies.Add(lobbyId, lobbyData);
		SpawnedNodes.Add(lobbyId, new());
		RemovedSceneNodes.Add(lobbyId, new());

		// add lobby node to game holder
		var lobbyNode = GD.Load<PackedScene>(lobbyData[MattohaLobbyKeys.LobbySceneFile].AsString()).Instantiate();
		if (lobbyNode is Node2D)
			(lobbyNode as Node2D).Position = new Vector2(CalculateLobbyXPosition(), 0);
		if (lobbyNode is Node3D)
			(lobbyNode as Node3D).Position = new Vector3(CalculateLobbyXPosition(), 0, 0);
		lobbyNode.Name = $"Lobby{lobbyId}";
		GameHolder.AddChild(lobbyNode);

		MiddlewareNode.AfterCreateLobby(lobbyData, sender);

		_system.SendReliableClientRpc(sender, nameof(ClientRpc.CreateLobby), lobbyData);
		_system.SendReliableClientRpc(sender, nameof(ClientRpc.SetPlayerData), player);
		RefreshAvailableLobbiesForAll();
#endif
	}


	private void RpcSetLobbyData(Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		var lobby = GetPlayerLobby(sender);
		if (lobby == null)
			return;
		if (lobby[MattohaLobbyKeys.OwnerId].AsInt64() != sender)
			return;
		
		var lobbyId = lobby[MattohaLobbyKeys.Id].AsInt32();
		var result = MiddlewareNode.BeforeSetLobbyData(lobbyId, payload, sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.SetLobbyDataFailed), result);
		foreach (var pair in payload)
		{
			if (MattohaLobbyKeys.FreezedProperties.Contains(pair.Key))
				continue;
			lobby[pair.Key] = pair.Value;
		}
		MiddlewareNode.AfterSetLobbyData(lobbyId, payload, sender);
		SendRpcForPlayersInLobby(lobbyId, nameof(ClientRpc.SetLobbyData), lobby, true, sender);
#endif
	}


	private void RpcSetLobbyOwner(Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		var lobby = GetPlayerLobby(sender);
		if (lobby == null)
			return;
		if (lobby[MattohaLobbyKeys.OwnerId].AsInt64() != sender)
			return;

		var result = MiddlewareNode.BeforeSetLobbyOwner(lobby, sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.SetLobbyOwnerFailed), result);
		lobby[MattohaLobbyKeys.OwnerId] = payload[MattohaLobbyKeys.OwnerId];
		MiddlewareNode.AfterSetLobbyOwner(lobby, sender);
		SendRpcForPlayersInLobby(lobby[MattohaLobbyKeys.Id].AsInt32(), nameof(ClientRpc.SetLobbyOwner), lobby, true, lobby[MattohaLobbyKeys.OwnerId].AsInt64());
#endif
	}


	public void RefreshAvailableLobbiesForAll()
	{
#if MATTOHA_SERVER
		if (_system.AutoLoadAvailableLobbies)
		{
			foreach (var playerId in Players.Keys)
			{
				RpcLoadAvailableLobbies(playerId);
			}
		}
#endif
	}

	private void RpcLoadAvailableLobbies(long sender)
	{
#if MATTOHA_SERVER

		var result = MiddlewareNode.BeforeLoadAvailableLobbies(sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.LoadAvailableLobbiesFailed), result);
		Array<Dictionary<string, Variant>> lobbies = new();
		foreach (var lobbyData in Lobbies.Values)
		{
			lobbies.Add(MattohaUtils.ToSecuredDict(lobbyData));
		}
		Dictionary<string, Variant> payload = new()
		{
			{ "Lobbies", lobbies }
		};
		MiddlewareNode.AfterLoadAvailableLobbies(sender);
		_system.SendReliableClientRpc(sender, nameof(ClientRpc.LoadAvailableLobbies), payload);
#endif
	}


	private void RpcJoinLobby(Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		if (!Players.TryGetValue(sender, out var player))
			return;
		var lobbyId = payload[MattohaLobbyKeys.Id].AsInt32();
		if (!Lobbies.TryGetValue(lobbyId, out var lobby))
			return;

		if (lobby[MattohaLobbyKeys.MaxPlayers].AsInt32() <= lobby[MattohaLobbyKeys.PlayersCount].AsInt32())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.JoinLobbyFailed), new Dictionary<string, Variant> { { "Message", "no-seats" } });


		var result = MiddlewareNode.BeforeJoinLobby(lobby, sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.JoinLobbyFailed), result);

		RemovePlayerFromLobby(sender);
			
		player[MattohaPlayerKeys.JoinedLobbyId] = lobbyId;
		lobby[MattohaLobbyKeys.PlayersCount] = lobby[MattohaLobbyKeys.PlayersCount].AsInt32() + 1;

		MiddlewareNode.AfterJoinLobby(lobby, sender);

		_system.SendReliableClientRpc(sender, nameof(ClientRpc.SetPlayerData), player);
		_system.SendReliableClientRpc(sender, nameof(ClientRpc.JoinLobby), MattohaUtils.ToSecuredDict(lobby));

		RefreshAvailableLobbiesForAll();
		SendRpcForPlayersInLobby(lobbyId, nameof(ClientRpc.NewPlayerJoined), player, true);
#endif
	}


	private void RpcStartGame(long sender)
	{
#if MATTOHA_SERVER
		var lobby = GetPlayerLobby(sender);
		if (lobby == null)
			return;

		if (lobby[MattohaLobbyKeys.OwnerId].AsInt64() != sender)
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.StartGameFailed), new Dictionary<string, Variant> { { "Message", "not-owner" } });

		var result = MiddlewareNode.BeforeStartGame(lobby, sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.StartGameFailed), result);

		MiddlewareNode.AfterStartGame(lobby, sender);
		lobby[MattohaLobbyKeys.IsGameStarted] = true;
		SendRpcForPlayersInLobby(lobby[MattohaLobbyKeys.Id].AsInt32(), nameof(ClientRpc.StartGame), null);

#endif
	}

	public void LoadLobbyPlayersForAll(int lobbyId)
	{
		var players = GetLobbyPlayersSecured(lobbyId);
		var payload = new Dictionary<string, Variant>()
		{
			{ "Players", players }
		};
		SendRpcForPlayersInLobby(lobbyId, nameof(ClientRpc.LoadLobbyPlayers), payload);
	}


	private void RpcLoadLobbyPlayers(long sender)
	{
#if MATTOHA_SERVER
		if (!Players.TryGetValue(sender, out var player))
			return;

		var result = MiddlewareNode.BeforeLoadLobbyPlayers(sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.LoadLobbyPlayersFailed), result);
		var joinedLobbyId = player[MattohaPlayerKeys.JoinedLobbyId].AsInt32();
		var players = GetLobbyPlayersSecured(joinedLobbyId);
		var payload = new Dictionary<string, Variant>()
		{
			{ "Players", players }
		};
		MiddlewareNode.AfterLoadLobbyPlayers(sender);
		_system.SendReliableClientRpc(sender, nameof(ClientRpc.LoadLobbyPlayers), payload);
#endif
	}


	private void RpcSpawnNode(Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		var lobbyId = MattohaSystem.ExtractLobbyId(payload[MattohaSpawnKeys.ParentPath].ToString());	
		if (!Lobbies.TryGetValue(lobbyId, out var lobby))
			return;

		var result = MiddlewareNode.BeforeSpawnNode(lobby, sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.SpawnNodeFailed), result);

		payload[MattohaSpawnKeys.Owner] = sender;
		_system.SpawnNodeFromPayload(payload);
		SpawnedNodes[lobbyId].Add(payload);
		
		MiddlewareNode.AfterSpawnNode(lobby, sender);
		SendRpcForPlayersInLobby(lobbyId, nameof(ClientRpc.SpawnNode), payload);
#endif
	}


	private void RpcSpawnLobbyNodes(long sender)
	{
#if MATTOHA_SERVER
		var lobby = GetPlayerLobby(sender);
		if (lobby == null)
			return;

		var result = MiddlewareNode.BeforeSpawnLobbyNodes(lobby, sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.SpawnLobbyNodesFailed), result);

		var lobbyId = lobby[MattohaLobbyKeys.Id].AsInt32();
		var spawnedNodes = SpawnedNodes[lobbyId];
		var payload = new Dictionary<string, Variant>()
		{
			{ "Nodes", spawnedNodes }
		};

		MiddlewareNode.AfterSpawnLobbyNodes(lobby, sender);
		_system.SendReliableClientRpc(sender, nameof(ClientRpc.SpawnLobbyNodes), payload);
#endif
	}


	private void RpcDespawnNode(Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		var lobbyId = MattohaSystem.ExtractLobbyId(payload[MattohaSpawnKeys.ParentPath].ToString());
		if (!Lobbies.TryGetValue(lobbyId, out var lobby))
			return;


		var result = MiddlewareNode.BeforeDespawnNode(lobby, sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.DespawnNodeFailed), result);

		var spawnedNode = FindSpawnedNode(payload);
		if (spawnedNode != null)
		{
			var nodeOwner = spawnedNode[MattohaSpawnKeys.Owner].AsInt64();
			if (nodeOwner == sender)
			{
				DespawnNode(payload);
				MiddlewareNode.AfterDespawnNode(lobby, sender);
			}
		}
#endif
	}

	public void DespawnNode(Dictionary<string, Variant> payload)
	{
#if MATTOHA_SERVER
		var spawnedNode = FindSpawnedNode(payload);
		var lobbyId = MattohaSystem.ExtractLobbyId(payload[MattohaSpawnKeys.ParentPath].ToString());
		// is spawnedNode is null then its not a scene nodes, meaning that this node spawned based on game logic from some player or from server
		if (spawnedNode != null)
		{
			SendRpcForPlayersInLobby(lobbyId, nameof(ClientRpc.DespawnNode), payload);
			_system.DespawnNodeFromPayload(spawnedNode);
			SpawnedNodes[lobbyId].Remove(payload);
		}
		else // now , we will assume that this node was already in scene structure, so we will add it to RemovedSceneNodes if not exists
		{
			spawnedNode = FindRemovedSceneNode(payload);
			if (spawnedNode == null)
			{
				if (RemovedSceneNodes.ContainsKey(lobbyId))
				{
					RemovedSceneNodes[lobbyId].Add(payload);
				}
			}
			_system.DespawnNodeFromPayload(payload);
			SendRpcForPlayersInLobby(lobbyId, nameof(ClientRpc.DespawnNode), payload);
		}
#endif
	}


	private void RpcDespawnRemovedSceneNodes(long sender)
	{
#if MATTOHA_SERVER
		var player = GetPlayer(sender);
		if (player == null)
			return;

		var lobbyId = player[MattohaPlayerKeys.JoinedLobbyId].AsInt32();
		if (lobbyId == 0)
			return;

		var result = MiddlewareNode.BeforeDespawnRemovedSceneNodes(lobbyId, sender);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.DespawnRemovedSceneNodesFailed), result);

		var removedNodes = RemovedSceneNodes[lobbyId];
		var payload = new Dictionary<string, Variant>()
		{
			{ "Nodes", removedNodes }
		};
		MiddlewareNode.AfterDespawnRemovedSceneNodes(lobbyId, sender);
		_system.SendReliableClientRpc(sender, nameof(ClientRpc.DespawnRemovedSceneNodes), payload);
#endif
	}


	private void RpcJoinTeam(Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		var player = GetPlayer(sender);
		if (player == null)
			return;
		var teamId = payload[MattohaPlayerKeys.TeamId];

		var result = MiddlewareNode.BeforeJoinTeam(sender, teamId);
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.JoinTeamFailed), result);

		player[MattohaPlayerKeys.TeamId] = teamId;

		MiddlewareNode.AfterJoinTeam(sender, teamId);

		_system.SendReliableClientRpc(sender, nameof(ClientRpc.JoinTeam), payload);
		SendRpcForPlayersInLobby(player[MattohaPlayerKeys.JoinedLobbyId].AsInt32(), nameof(ClientRpc.PlayerChangedHisTeam), player, true);
#endif
	}


	private void RpcSendGlobalMessage(Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		var result = MiddlewareNode.BeforeSendGlobalMessage(sender, payload["Message"].ToString());
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.SendGlobalMessageFailed), result);

		payload["Player"] = MattohaUtils.ToChatDict(GetPlayer(sender));
		
		MiddlewareNode.AfterSendGlobalMessage(sender, payload["Message"].ToString());
		_system.SendReliableClientRpc(0, nameof(ClientRpc.SendGlobalMessage), payload);
#endif
	}

	private void RpcSendLobbyMessage(Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		payload["Player"] = MattohaUtils.ToChatDict(GetPlayer(sender));
		var lobby = GetPlayerLobby(sender);
		if (lobby == null)
			return;

		var result = MiddlewareNode.BeforeSendLobbyMessage(lobby, sender, payload["Message"].ToString());
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.SendLobbyMessageFailed), result);

		var lobbyId = lobby[MattohaLobbyKeys.Id].AsInt32();
		var chatDictPlayer = MattohaUtils.ToChatDict(GetPlayer(sender));
		payload["Player"] = chatDictPlayer;

		MiddlewareNode.AfterSendLobbyMessage(lobby, sender, payload["Message"].ToString());
		SendRpcForPlayersInLobby(lobbyId, nameof(ClientRpc.SendLobbyMessage), payload);
#endif
	}

	private void RpcSendTeamMessage(Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		var playerDict = GetPlayer(sender);
		payload["Player"] = MattohaUtils.ToChatDict(playerDict);
		var lobby = GetPlayerLobby(sender);
		if (lobby == null)
			return;

		var chatDictPlayer = MattohaUtils.ToChatDict(playerDict);
		var lobbyId = lobby[MattohaLobbyKeys.Id].AsInt32();
		var teamId = playerDict[MattohaPlayerKeys.TeamId].AsInt32();

		var result = MiddlewareNode.BeforeSendTeamMessage(lobby, teamId, sender, payload["Message"].ToString());
		if (!result["Status"].AsBool())
			_system.SendReliableClientRpc(sender, nameof(ClientRpc.SendTeamMessageFailed), result);

		payload["Player"] = chatDictPlayer;
		var players = GetLobbyPlayers(lobbyId);

		MiddlewareNode.AfterSendTeamMessage(lobby, teamId, sender, payload["Message"].ToString());
		foreach (var player in players)
		{
			if (player[MattohaPlayerKeys.TeamId].AsInt32() != teamId)
				continue;
			_system.SendReliableClientRpc(player[MattohaPlayerKeys.Id].AsInt64(), nameof(ClientRpc.SendTeamMessage), payload);
		}
#endif
	}

	private void RpcLeaveLobby(long sender)
	{
#if MATTOHA_SERVER
		RemovePlayerFromLobby(sender);
#endif
	}

	public void RemovePlayerFromLobby(long playerId)
	{
#if MATTOHA_SERVER
		var player = GetPlayer(playerId);
		if (player == null)
			return;

		if (player[MattohaPlayerKeys.JoinedLobbyId].AsInt32() == 0)
			return;

		var lobby = GetPlayerLobby(playerId);
		if (lobby == null)
			return;

		MiddlewareNode.BeforeRemovePlayerFromLobby(playerId);
		var playerNodes = new Array<Dictionary<string, Variant>>();
		var spawnedNodes = SpawnedNodes[lobby[MattohaLobbyKeys.Id].AsInt32()];
		
		foreach (var playerNode in spawnedNodes)
		{
			if (playerNode[MattohaSpawnKeys.Owner].AsInt64() == playerId)
			{
				playerNodes.Add(playerNode);
			}
		}

		player[MattohaPlayerKeys.JoinedLobbyId] = 0;
		lobby[MattohaLobbyKeys.PlayersCount] = lobby[MattohaLobbyKeys.PlayersCount].AsInt32() - 1;
		var lobbyId = lobby[MattohaLobbyKeys.Id].AsInt32();
		if (lobby[MattohaLobbyKeys.PlayersCount].AsInt32() == 0)
		{
			Lobbies.Remove(lobby[MattohaLobbyKeys.Id].AsInt32());
			GameHolder.GetNode($"Lobby{lobbyId}").QueueFree();
			SpawnedNodes.Remove(lobbyId);
			RemovedSceneNodes.Remove(lobbyId);
		}
		else if (lobby[MattohaLobbyKeys.OwnerId].AsInt64() == playerId)
		{
			var lobbyPlayers = GetLobbyPlayers(lobbyId);
			lobby[MattohaLobbyKeys.OwnerId] = lobbyPlayers[0][MattohaPlayerKeys.Id].AsInt64();
		}

		// despawn player nodes
		foreach(var node in playerNodes)
		{
			DespawnNode(node);
		}

		MiddlewareNode.AfterRemovePlayerFromLobby(playerId);
		_system.SendReliableClientRpc(playerId, nameof(ClientRpc.LeaveLobby), player);
		SendRpcForPlayersInLobby(lobbyId, nameof(ClientRpc.PlayerLeft), player, true);
		SendRpcForPlayersInLobby(lobbyId, nameof(ClientRpc.SetLobbyData), lobby, true, lobby[MattohaLobbyKeys.OwnerId].AsInt64());
		LoadLobbyPlayersForAll(lobbyId);
		RefreshAvailableLobbiesForAll();
#endif
	}


	private void OnServerRpcRecieved(string methodName, Dictionary<string, Variant> payload, long sender)
	{
#if MATTOHA_SERVER
		switch (methodName)
		{
			case nameof(ServerRpc.SetPlayerData):
				RpcSetPlayerData(payload, sender);
				break;
			case nameof(ServerRpc.CreateLobby):
				RpcCreateLobby(payload, sender);
				break;
			case nameof(ServerRpc.SetLobbyData):
				RpcSetLobbyData(payload, sender);
				break;
			case nameof(ServerRpc.SetLobbyOwner):
				RpcSetLobbyOwner(payload, sender);
				break;
			case nameof(ServerRpc.LoadAvailableLobbies):
				RpcLoadAvailableLobbies(sender);
				break;
			case nameof(ServerRpc.JoinLobby):
				RpcJoinLobby(payload, sender);
				break;
			case nameof(ServerRpc.JoinTeam):
				RpcJoinTeam(payload, sender);
				break;
			case nameof(ServerRpc.StartGame):
				RpcStartGame(sender);
				break;
			case nameof(ServerRpc.LoadLobbyPlayers):
				RpcLoadLobbyPlayers(sender);
				break;
			case nameof(ServerRpc.SpawnNode):
				RpcSpawnNode(payload, sender);
				break;
			case nameof(ServerRpc.SpawnLobbyNodes):
				RpcSpawnLobbyNodes(sender);
				break;
			case nameof(ServerRpc.DespawnNode):
				RpcDespawnNode(payload, sender);
				break;
			case nameof(ServerRpc.DespawnRemovedSceneNodes):
				RpcDespawnRemovedSceneNodes(sender);
				break;
			case nameof(ServerRpc.SendTeamMessage):
				RpcSendTeamMessage(payload, sender);
				break;
			case nameof(ServerRpc.SendLobbyMessage):
				RpcSendLobbyMessage(payload, sender);
				break;
			case nameof(ServerRpc.SendGlobalMessage):
				RpcSendGlobalMessage(payload, sender);
				break;
			case nameof(ServerRpc.LeaveLobby):
				RpcLeaveLobby(sender);
				break;
		}
#endif
	}
}
