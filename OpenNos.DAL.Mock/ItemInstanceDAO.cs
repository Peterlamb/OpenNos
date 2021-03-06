﻿using AutoMapper;
using OpenNos.DAL.Interface;
using OpenNos.Data;
using OpenNos.Data.Enums;
using OpenNos.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNos.DAL.Mock
{
    public class ItemInstanceDAO : SynchronizableBaseDAO<ItemInstanceDTO>, IItemInstanceDAO
    {
        #region Members

        private Type _baseType;
        private IDictionary<Type, Type> itemInstanceMappings = new Dictionary<Type, Type>();

        #endregion

        #region Methods

        public DeleteResult DeleteFromSlotAndType(long characterId, short slot, InventoryType type)
        {
            throw new NotImplementedException();
        }

        IList<Guid> IItemInstanceDAO.LoadSlotAndTypeByCharacterId(long characterId)
        {
            throw new NotImplementedException();
        }

        public void InitializeMapper(Type baseType)
        {
            _baseType = baseType;
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap(baseType, typeof(ItemInstanceDTO));
                cfg.CreateMap(typeof(ItemInstanceDTO), typeof(ItemInstanceDTO)).As(baseType);
                Type itemInstanceType = typeof(ItemInstanceDTO);
                foreach (KeyValuePair<Type, Type> entry in itemInstanceMappings)
                {
                    cfg.CreateMap(entry.Key, entry.Value).IncludeBase(baseType, typeof(ItemInstanceDTO));
                    cfg.CreateMap(entry.Value, entry.Key).IncludeBase(typeof(ItemInstanceDTO), baseType);
                    Type retrieveDTOType = Type.GetType($"OpenNos.Data.{entry.Key.Name}DTO, OpenNos.Data");
                    cfg.CreateMap(entry.Value, typeof(ItemInstanceDTO)).As(entry.Key);
                }
            });
            _mapper = config.CreateMapper();
        }

        public IEnumerable<ItemInstanceDTO> LoadByCharacterId(long characterId)
        {
            return Container.Where(i => i.CharacterId == characterId);
        }

        public ItemInstanceDTO LoadBySlotAndType(long characterId, short slot, InventoryType type)
        {
            return MapEntity(Container.SingleOrDefault(i => i.CharacterId == characterId && i.Slot == slot && i.Type == type));
        }

        public IEnumerable<ItemInstanceDTO> LoadByType(long characterId, InventoryType type)
        {
            return Container.Where(i => i.CharacterId == characterId && i.Type == type);
        }

        public IEnumerable<Guid> LoadSlotAndTypeByCharacterId(long characterId)
        {
            return Container.Where(i => i.CharacterId == characterId).Select(c => c.Id);
        }

        public override IMappingBaseDAO RegisterMapping(Type gameObjectType)
        {
            Type itemInstanceType = typeof(ItemInstanceDTO);
            if (!itemInstanceMappings.ContainsKey(gameObjectType))
            {
                itemInstanceMappings.Add(gameObjectType, itemInstanceType);
            }

            return (IMappingBaseDAO)this;
        }

        #endregion
    }
}